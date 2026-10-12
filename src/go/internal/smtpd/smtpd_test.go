package smtpd

import (
	"context"
	"encoding/base64"
	"encoding/json"
	"io"
	"log/slog"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"
)

var discard = slog.New(slog.NewTextHandler(io.Discard, nil))

func TestIsAllowedDomain(t *testing.T) {
	allowed := []string{"mailvoid.com", "example.org"}
	cases := map[string]bool{
		"mailvoid.com":          true,  // exact match
		"MailVoid.com":          true,  // case-insensitive
		"sub.mailvoid.com":      true,  // subdomain
		"a.b.mailvoid.com":      true,  // nested subdomain
		"example.org":           true,  // second allowed domain
		"notmailvoid.com":       false, // lookalike must not match (open-relay guard)
		"mailvoid.com.evil.com": false, // suffix trickery
		"evil.com":              false,
		"":                      false,
	}
	for domain, want := range cases {
		if got := IsAllowedDomain(domain, allowed); got != want {
			t.Errorf("IsAllowedDomain(%q) = %v, want %v", domain, got, want)
		}
	}
	if IsAllowedDomain("mailvoid.com", nil) {
		t.Error("empty allow list must reject everything")
	}
}

func TestIsValidEmailFormat(t *testing.T) {
	cases := map[string]bool{
		"user@mailvoid.com":               true,
		"first.last+tag@sub.mailvoid.com": true,
		"no-at-sign":                      false,
		"@nolocal.com":                    false,
		"nodomain@":                       false,
		"two@@at.com":                     false,
		"nodot@localhost":                 false,
		"has space@mailvoid.com":          false,
		"angle<@mailvoid.com":             false,
		"":                                false,
	}
	for email, want := range cases {
		if got := IsValidEmailFormat(email); got != want {
			t.Errorf("IsValidEmailFormat(%q) = %v, want %v", email, got, want)
		}
	}
}

func TestMailboxFilter(t *testing.T) {
	f := NewMailboxFilter(discard, FilterOptions{AllowedDomains: []string{"mailvoid.com"}, BlockedDomains: []string{"blocked.com"}}, 100)
	if f.CanAcceptFrom("a@blocked.com", 0) {
		t.Error("blocked sender domain accepted")
	}
	if f.CanAcceptFrom("a@ok.com", 101) {
		t.Error("oversized SIZE accepted")
	}
	if !f.CanAcceptFrom("a@ok.com", 50) {
		t.Error("valid sender rejected")
	}
	if f.CanDeliverTo("x@other.com", "a@ok.com") {
		t.Error("relay to non-test domain accepted")
	}
	if f.CanDeliverTo("x@mailvoid.com", "X@mailvoid.com") {
		t.Error("mail loop accepted")
	}
	if !f.CanDeliverTo("x@sub.mailvoid.com", "a@ok.com") {
		t.Error("valid recipient rejected")
	}
}

const multipartWithAttachment = "From: Sender Name <sender@example.com>\r\n" +
	"To: recipient@mailvoid.com\r\n" +
	"Subject: =?utf-8?B?SGVsbG8gd29ybGQ=?=\r\n" +
	"Message-ID: <abc123@example.com>\r\n" +
	"Date: Mon, 10 Aug 2026 18:36:21 +0000\r\n" +
	"DKIM-Signature: v=1; a=rsa-sha256;\r\n\th=from:subject\r\n" +
	"Received: by one\r\n" +
	"Received: by two\r\n" +
	"MIME-Version: 1.0\r\n" +
	"Content-Type: multipart/mixed; boundary=\"BOUND\"\r\n" +
	"\r\n" +
	"--BOUND\r\n" +
	"Content-Type: multipart/alternative; boundary=\"ALT\"\r\n" +
	"\r\n" +
	"--ALT\r\n" +
	"Content-Type: text/plain; charset=utf-8\r\n" +
	"\r\n" +
	"Hello in plain text\r\n" +
	"--ALT\r\n" +
	"Content-Type: text/html; charset=utf-8\r\n" +
	"\r\n" +
	"<p>Hello in HTML</p>\r\n" +
	"--ALT--\r\n" +
	"--BOUND\r\n" +
	"Content-Type: text/plain; name=\"note.txt\"\r\n" +
	"Content-Disposition: attachment; filename=\"note.txt\"\r\n" +
	"Content-Transfer-Encoding: base64\r\n" +
	"\r\n" +
	"ZmlsZSBib2R5\r\n" +
	"--BOUND--\r\n"

func TestBuildEmailData_Multipart(t *testing.T) {
	d, err := BuildEmailData([]byte(multipartWithAttachment), "envelope@example.com", []string{"recipient@mailvoid.com"})
	if err != nil {
		t.Fatal(err)
	}
	if d.From != "sender@example.com" {
		t.Errorf("From = %q", d.From)
	}
	if d.Subject != "Hello world" {
		t.Errorf("Subject = %q", d.Subject)
	}
	if d.Text == nil || *d.Text != "Hello in plain text" {
		t.Errorf("Text = %v", d.Text)
	}
	if d.Html == nil || *d.Html != "<p>Hello in HTML</p>" {
		t.Errorf("Html = %v", d.Html)
	}
	if d.MessageId == nil || *d.MessageId != "abc123@example.com" {
		t.Errorf("MessageId = %v", d.MessageId)
	}
	if want := time.Date(2026, 8, 10, 18, 36, 21, 0, time.UTC); !d.Date.Equal(want) {
		t.Errorf("Date = %v", d.Date)
	}
	if len(d.Attachments) != 1 {
		t.Fatalf("attachments = %d", len(d.Attachments))
	}
	a := d.Attachments[0]
	body, _ := base64.StdEncoding.DecodeString(a.Content)
	if a.Filename != "note.txt" || a.ContentType != "text/plain" || string(body) != "file body" {
		t.Errorf("attachment = %+v (%q)", a, body)
	}
	// Original header casing, unfolded values (tab kept), repeated headers joined by \n.
	if got, _ := d.Headers.Get("DKIM-Signature"); got != "v=1; a=rsa-sha256;\th=from:subject" {
		t.Errorf("DKIM-Signature = %q", got)
	}
	if got, _ := d.Headers.Get("Received"); got != "by one\nby two" {
		t.Errorf("Received = %q", got)
	}
	if got, _ := d.Headers.Get("Message-ID"); got != "<abc123@example.com>" {
		t.Errorf("Message-ID = %q", got)
	}
	// Headers keep message order (Dictionary insertion order in .NET), not sorted order.
	if keys := d.Headers.Keys(); len(keys) < 4 || keys[0] != "From" || keys[1] != "To" || keys[2] != "Subject" || keys[3] != "Message-ID" {
		t.Errorf("header order = %v", keys)
	}
	b, _ := json.Marshal(d.Headers)
	if !strings.HasPrefix(string(b), `{"From":`) {
		t.Errorf("headers JSON = %s", b)
	}
}

func TestBuildEmailData_PlainOnly(t *testing.T) {
	raw := "From: a@example.com\r\nTo: b@mailvoid.com\r\nSubject: Plain\r\nContent-Type: text/plain; charset=utf-8\r\n\r\njust text"
	d, err := BuildEmailData([]byte(raw), "", []string{"b@mailvoid.com"})
	if err != nil {
		t.Fatal(err)
	}
	if d.Text == nil || *d.Text != "just text" {
		t.Errorf("Text = %v", d.Text)
	}
	if d.Html != nil {
		t.Errorf("Html = %q, want nil", *d.Html)
	}
	if len(d.Attachments) != 0 {
		t.Errorf("attachments = %d", len(d.Attachments))
	}
}

func TestBuildEmailData_HtmlOnlyTextFallsBackToHtml(t *testing.T) {
	raw := "From: a@example.com\r\nSubject: H\r\nContent-Type: text/html; charset=utf-8\r\n\r\n<b>hi</b>"
	d, _ := BuildEmailData([]byte(raw), "", []string{"b@mailvoid.com"})
	if d.Html == nil || *d.Html != "<b>hi</b>" || d.Text == nil || *d.Text != "<b>hi</b>" {
		t.Errorf("Html=%v Text=%v", d.Html, d.Text)
	}
}

func TestBuildEmailData_RawEmailIsLatin1(t *testing.T) {
	raw := []byte("Subject: x\r\n\r\n\xe9\xff")
	d, _ := BuildEmailData(raw, "", []string{"b@mailvoid.com"})
	if d.RawEmail != "Subject: x\r\n\r\néÿ" {
		t.Errorf("RawEmail = %q", d.RawEmail)
	}
}

func newTestQueue(t *testing.T, dir string, maxRetries int) *Queue {
	t.Helper()
	q, err := NewQueue(discard, QueueOptions{MaxRetryAttempts: maxRetries, BaseRetryDelaySeconds: 1, MaxQueueDepth: 100, SpoolDirectory: dir})
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(q.Close)
	return q
}

func sample() EmailWebhookData {
	return EmailWebhookData{From: "sender@example.com", To: []string{"recipient@mailvoid.com"}, Subject: "test"}
}

func TestQueue_EnqueuePersistsBeforeReturning(t *testing.T) {
	dir := t.TempDir()
	q := newTestQueue(t, dir, 3)
	if _, err := q.Enqueue(context.Background(), sample()); err != nil {
		t.Fatal(err)
	}
	if files, _ := filepath.Glob(filepath.Join(dir, "*.json")); len(files) != 1 {
		t.Fatalf("spool files = %d", len(files))
	}
}

// loadNext pops the next entry and reads its content from the spool, as a worker does.
func loadNext(t *testing.T, q *Queue) *QueuedEmail {
	t.Helper()
	select {
	case e := <-q.Items():
		item, err := q.Load(e)
		if err != nil {
			t.Fatal(err)
		}
		return item
	case <-time.After(2 * time.Second):
		t.Fatal("no queued item")
	}
	return nil
}

func TestQueue_MarkFailedDeadLettersAfterMaxRetries(t *testing.T) {
	dir := t.TempDir()
	q := newTestQueue(t, dir, 3)
	q.Enqueue(context.Background(), sample())
	item := loadNext(t, q)

	for i := 1; i <= 2; i++ {
		q.MarkFailed(item, "boom")
		if total, _ := q.DeadLetterStats(); item.Attempts != i || total != 0 {
			t.Fatalf("after %d failures: attempts=%d dead=%d", i, item.Attempts, total)
		}
		// Retrying entries are counted once, not in both the pending counter and the retry map.
		if got := q.PendingCount(); got != 1 {
			t.Fatalf("PendingCount while retrying = %d, want 1", got)
		}
		// Retry timer fires; the worker reloads the message (and its attempt count) from disk.
		q.requeueDue(time.Now().Add(time.Hour))
		item = loadNext(t, q)
		if item.Attempts != i {
			t.Fatalf("reloaded attempts = %d, want %d", item.Attempts, i)
		}
	}
	q.MarkFailed(item, "boom")
	if total, _ := q.DeadLetterStats(); item.Attempts != 3 || total != 1 {
		t.Fatalf("attempts=%d dead=%d", item.Attempts, total)
	}
	if got := q.PendingCount(); got != 0 {
		t.Fatalf("PendingCount after dead-letter = %d", got)
	}
	if files, _ := filepath.Glob(filepath.Join(dir, "dead-letter", "*.json")); len(files) != 1 {
		t.Fatalf("dead-letter files = %d", len(files))
	}
	if files, _ := filepath.Glob(filepath.Join(dir, "*.json")); len(files) != 0 {
		t.Fatalf("spool files left = %d", len(files))
	}
}

func TestQueue_MarkProcessedDeletesSpoolFile(t *testing.T) {
	dir := t.TempDir()
	q := newTestQueue(t, dir, 3)
	q.Enqueue(context.Background(), sample())
	q.MarkProcessed(loadNext(t, q))
	if files, _ := filepath.Glob(filepath.Join(dir, "*.json")); len(files) != 0 {
		t.Fatalf("spool files = %d", len(files))
	}
	if got := q.PendingCount(); got != 0 {
		t.Fatalf("PendingCount = %d", got)
	}
}

func TestQueue_LoadSpoolRecoversItems(t *testing.T) {
	dir := t.TempDir()
	newTestQueue(t, dir, 3).Enqueue(context.Background(), sample())

	recovered := newTestQueue(t, dir, 3)
	recovered.ScanSpool()
	recovered.LoadSpool(context.Background())
	if item := loadNext(t, recovered); item.EmailData.From != "sender@example.com" {
		t.Fatalf("From = %q", item.EmailData.From)
	}
}

// A message accepted after the startup scan must not also be picked up by LoadSpool.
func TestQueue_StartupScanDoesNotDoubleDeliver(t *testing.T) {
	dir := t.TempDir()
	newTestQueue(t, dir, 3).Enqueue(context.Background(), sample()) // left over from a previous run

	q := newTestQueue(t, dir, 3)
	q.ScanSpool()
	q.Enqueue(context.Background(), sample()) // accepted during startup
	q.LoadSpool(context.Background())

	if n := len(q.Items()); n != 2 {
		t.Fatalf("queued entries = %d, want 2 (one leftover + one new)", n)
	}
}

func TestQueue_RemovesTempFilesAtStartup(t *testing.T) {
	dir := t.TempDir()
	os.WriteFile(filepath.Join(dir, "abc.json.tmp"), []byte("{partial"), 0o644)
	newTestQueue(t, dir, 3)
	if files, _ := filepath.Glob(filepath.Join(dir, "*.tmp")); len(files) != 0 {
		t.Fatalf("temp files left = %v", files)
	}
}

// Spool files keep <, > and & unescaped so HTML mail doesn't bloat 6x on disk.
func TestQueue_SpoolDoesNotEscapeHTML(t *testing.T) {
	dir := t.TempDir()
	q := newTestQueue(t, dir, 3)
	d := sample()
	d.Html = strPtr("<p>a & b</p>")
	q.Enqueue(context.Background(), d)
	files, _ := filepath.Glob(filepath.Join(dir, "*.json"))
	b, _ := os.ReadFile(files[0])
	if !strings.Contains(string(b), "<p>a & b</p>") {
		t.Fatalf("spool file escaped HTML: %s", b)
	}
}

// A spool file as written by the .NET EmailSpoolQueue must be readable.
func TestQueue_LoadsDotNetSpoolFile(t *testing.T) {
	dir := t.TempDir()
	dotnet := `{"Id":"5b9a4f6e-1c0d-4a8e-9f00-3c2b1a0d9e8f","EmailData":{"From":"a@example.com","To":["b@mailvoid.com"],"Subject":"[parse failed]","Html":null,"Text":"x","Headers":{},"Attachments":[],"MessageId":null,"Date":"0001-01-01T00:00:00","RawEmail":"raw","ParseFailed":true},"QueuedAt":"2026-08-10T18:36:21.1234567Z","Attempts":1,"LastAttemptAt":"2026-08-10T18:36:26.5Z","LastError":"API forwarding returned failure"}`
	if err := os.WriteFile(filepath.Join(dir, "5b9a4f6e-1c0d-4a8e-9f00-3c2b1a0d9e8f.json"), []byte(dotnet), 0o644); err != nil {
		t.Fatal(err)
	}
	q := newTestQueue(t, dir, 3)
	q.ScanSpool()
	q.LoadSpool(context.Background())
	item := loadNext(t, q)
	if item.Attempts != 1 || !item.EmailData.ParseFailed || item.EmailData.To[0] != "b@mailvoid.com" || item.QueuedAt.Year() != 2026 {
		t.Fatalf("unexpected item: %+v", item)
	}

	// And what we write back keeps the PascalCase contract.
	b, _ := json.Marshal(item)
	var m map[string]any
	json.Unmarshal(b, &m)
	for _, k := range []string{"Id", "EmailData", "QueuedAt", "Attempts", "LastAttemptAt", "LastError"} {
		if _, ok := m[k]; !ok {
			t.Errorf("missing key %s in %s", k, b)
		}
	}
}

func TestCertbotArgs(t *testing.T) {
	p := 80
	l := NewLetsEncrypt(discard, LetsEncryptOptions{Email: "a@b.com", ChallengeMethod: "dns-cloudflare", CertificateDirectory: "/le", HttpPort: &p}, func() {})
	got := l.certbotArgs("smtp.x.com", false)
	want := []string{"certonly", "--non-interactive", "--agree-tos", "--email", "a@b.com",
		"--dns-cloudflare", "--dns-cloudflare-credentials", filepath.Join("/le", "cloudflare.ini"), "--domains", "smtp.x.com",
		"--work-dir", "/le/work", "--config-dir", "/le/config", "--logs-dir", "/le/logs"}
	if len(got) != len(want) {
		t.Fatalf("args = %v", got)
	}
	for i := range want {
		if got[i] != want[i] {
			t.Fatalf("args = %v", got)
		}
	}
}
