package smtpd

import (
	"context"
	"crypto/tls"
	"errors"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
	"time"

	"github.com/emersion/go-smtp"
)

// nestedMultipart builds a message with depth nested multipart/mixed containers.
func nestedMultipart(depth int) string {
	var sb strings.Builder
	sb.WriteString("From: a@example.com\r\nSubject: bomb\r\nMIME-Version: 1.0\r\n")
	sb.WriteString("Content-Type: multipart/mixed; boundary=\"b0\"\r\n\r\n")
	for i := 1; i <= depth; i++ {
		sb.WriteString("--b" + strconv.Itoa(i-1) + "\r\n")
		sb.WriteString("Content-Type: multipart/mixed; boundary=\"b" + strconv.Itoa(i) + "\"\r\n\r\n")
	}
	sb.WriteString("--b" + strconv.Itoa(depth) + "\r\nContent-Type: text/plain\r\n\r\nhi\r\n")
	for i := depth; i >= 0; i-- {
		sb.WriteString("--b" + strconv.Itoa(i) + "--\r\n")
	}
	return sb.String()
}

// A deeply nested multipart message skips MIME parsing and is forwarded raw, quickly.
func TestBuildEmailData_NestingBombIsNotParsed(t *testing.T) {
	raw := []byte(nestedMultipart(50_000))
	start := time.Now()
	d, err := BuildEmailData(raw, "a@example.com", []string{"b@mailvoid.com"})
	if el := time.Since(start); el > 2*time.Second {
		t.Fatalf("BuildEmailData took %v", el)
	}
	if !errors.Is(err, ErrTooManyMimeParts) || !d.ParseFailed || d.RawEmail == "" {
		t.Fatalf("err=%v parseFailed=%v rawLen=%d", err, d.ParseFailed, len(d.RawEmail))
	}

	// Moderate nesting under the cap still parses.
	if d, err := BuildEmailData([]byte(nestedMultipart(10)), "", []string{"b@mailvoid.com"}); err != nil || d.ParseFailed || d.Text == nil || *d.Text != "hi" {
		t.Fatalf("10-level message: err=%v parseFailed=%v", err, d.ParseFailed)
	}
	// A cap <= 0 disables the check.
	if _, err := BuildEmailDataLimited([]byte(nestedMultipart(10)), "", []string{"b@mailvoid.com"}, 5); !errors.Is(err, ErrTooManyMimeParts) {
		t.Fatalf("cap of 5 not applied: %v", err)
	}
}

func TestCountBoundaryParams(t *testing.T) {
	cases := map[string]int{
		`Content-Type: multipart/mixed; boundary="x"`:     1,
		`Content-Type: multipart/mixed; BOUNDARY = "x"`:   1,
		`Content-Type: multipart/mixed; boundary*0="x"`:   1,
		"the boundary of the field is fine":               0,
		`boundary="a" boundary="b" boundary="c" boundary`: 3,
	}
	for in, want := range cases {
		if got := countBoundaryParams([]byte(in), 100); got != want {
			t.Errorf("countBoundaryParams(%q) = %d, want %d", in, got, want)
		}
	}
}

// With every DATA slot taken, a new transaction gets a temporary 451 4.3.2 instead of
// buffering another message in memory.
func TestServer_DataConcurrencyLimit(t *testing.T) {
	q := newTestQueue(t, t.TempDir(), 3)
	opts := ServerOptions{MaxConcurrentMessages: 1, MaxMessageSize: 1 << 20}
	s := NewServer(context.Background(), discard, opts, NewMailboxFilter(discard, FilterOptions{AllowedDomains: []string{"mailvoid.com"}}, 1<<20), q, nil)
	s.dataSlotWait = 50 * time.Millisecond

	release, ok := s.acquireDataSlot()
	if !ok {
		t.Fatal("first slot should be free")
	}
	ss := &session{s: s, remote: "test", from: "a@example.com", rcpts: []string{"b@mailvoid.com"}}
	start := time.Now()
	err := ss.Data(strings.NewReader("Subject: x\r\n\r\nbody\r\n"))
	var se *smtp.SMTPError
	if !errors.As(err, &se) || se.Code != 451 || se.EnhancedCode != (smtp.EnhancedCode{4, 3, 2}) {
		t.Fatalf("err = %v, want 451 4.3.2", err)
	}
	if el := time.Since(start); el > 2*time.Second {
		t.Fatalf("busy reply took %v", el)
	}

	release()
	release2, ok := s.acquireDataSlot()
	if !ok {
		t.Fatal("slot not released")
	}
	release2()
}

// A full channel doesn't block Enqueue forever: the durable entry is handed to the retry
// loop and queued once there's room, exactly once.
func TestQueue_EnqueueDoesNotBlockWhenFull(t *testing.T) {
	q, err := NewQueue(discard, QueueOptions{MaxRetryAttempts: 3, BaseRetryDelaySeconds: 1, MaxQueueDepth: 1, SpoolDirectory: t.TempDir()})
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(q.Close)
	q.enqueueWait = 50 * time.Millisecond

	first, _ := q.Enqueue(context.Background(), sample())
	start := time.Now()
	second, err := q.Enqueue(context.Background(), sample())
	if err != nil || time.Since(start) > 2*time.Second {
		t.Fatalf("second Enqueue: err=%v took %v", err, time.Since(start))
	}
	if got := q.PendingCount(); got != 2 {
		t.Fatalf("PendingCount = %d, want 2", got)
	}

	if item := loadNext(t, q); item.Id != first {
		t.Fatalf("first item = %s", item.Id)
	}
	q.requeueDue(time.Now())
	if item := loadNext(t, q); item.Id != second {
		t.Fatalf("second item = %s", item.Id)
	}
	q.requeueDue(time.Now())
	if n := len(q.Items()); n != 0 {
		t.Fatalf("entry queued twice (%d extra)", n)
	}
}

// A corrupt spool file is moved to dead-letter/ instead of failing on every restart.
func TestQueue_CorruptSpoolFileIsDeadLettered(t *testing.T) {
	dir := t.TempDir()
	os.WriteFile(filepath.Join(dir, "bad.json"), []byte("{not json"), 0o600)
	q := newTestQueue(t, dir, 3)
	q.ScanSpool()
	q.LoadSpool(context.Background())
	e := <-q.Items()
	if _, err := q.Load(e); err == nil {
		t.Fatal("corrupt file loaded")
	}
	if _, err := os.Stat(filepath.Join(dir, "dead-letter", "bad.json")); err != nil {
		t.Fatalf("not moved to dead-letter: %v", err)
	}
	if _, err := os.Stat(filepath.Join(dir, "bad.json")); !os.IsNotExist(err) {
		t.Fatal("corrupt file still in the spool")
	}
	if total, _ := q.DeadLetterStats(); total != 1 || q.PendingCount() != 0 {
		t.Fatalf("dead=%d pending=%d", total, q.PendingCount())
	}
}

func TestQueue_PrunesDeadLetters(t *testing.T) {
	dir := t.TempDir()
	q, err := NewQueue(discard, QueueOptions{MaxRetryAttempts: 3, MaxQueueDepth: 10, SpoolDirectory: dir,
		DeadLetterRetentionDays: 7, MaxDeadLetterFiles: 2})
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(q.Close)
	dead := filepath.Join(dir, "dead-letter")
	now := time.Now()
	for i, age := range []time.Duration{30 * 24 * time.Hour, 3 * time.Hour, 2 * time.Hour, time.Hour} {
		p := filepath.Join(dead, "m"+strconv.Itoa(i)+".json")
		os.WriteFile(p, []byte("{}"), 0o600)
		os.Chtimes(p, now.Add(-age), now.Add(-age))
	}
	os.WriteFile(filepath.Join(dead, "notes.txt"), []byte("keep"), 0o600)
	os.Chtimes(filepath.Join(dead, "notes.txt"), now.AddDate(-1, 0, 0), now.AddDate(-1, 0, 0))

	q.pruneDeadLetters(now)
	left, _ := filepath.Glob(filepath.Join(dead, "*.json"))
	for i := range left {
		left[i] = filepath.Base(left[i])
	}
	// m0 is past retention; m1 is the oldest beyond the 2-file cap.
	if strings.Join(left, ",") != "m2.json,m3.json" {
		t.Fatalf("left = %v", left)
	}
	if _, err := os.Stat(filepath.Join(dead, "notes.txt")); err != nil {
		t.Fatal("non-spool files must not be pruned")
	}
}

// Encoding the shared part once per message must produce the same wire JSON as encoding
// the whole mailData per recipient.
func TestPayloadEncoder_MatchesFullEncoding(t *testing.T) {
	email := twoRecipients()
	email.From = `quote"<&>@example.com`
	email.Html = strPtr("<p>a & b</p>")
	email.Headers.Set("Subject", "x")
	email.Attachments = []AttachmentData{{Filename: "f.txt", ContentType: "text/plain", Content: "Zm9v"}}
	email.RawEmail = "raw é"
	enc, err := newPayloadEncoder(email)
	if err != nil {
		t.Fatal(err)
	}
	for _, rcpt := range email.To {
		want, _ := encodeJSON(mailData{From: email.From, To: rcpt, Headers: email.Headers, Html: email.Html, Text: email.Text,
			Attachments: email.Attachments, RawEmail: &email.RawEmail, MessageId: email.MessageId})
		p, err := enc.forRecipient(rcpt)
		if err != nil {
			t.Fatal(err)
		}
		if got := p.Bytes(); string(got) != string(want) {
			t.Fatalf("payload mismatch:\n got %s\nwant %s", got, want)
		}
	}
}

func TestRequestTimeoutScalesWithPayload(t *testing.T) {
	if requestTimeout(1000) != forwardBaseTimeout+forwardPerMBTimeout {
		t.Fatalf("small payload timeout = %v", requestTimeout(1000))
	}
	if d := requestTimeout(40 << 20); d != forwardBaseTimeout+40*forwardPerMBTimeout {
		t.Fatalf("40 MB timeout = %v", d)
	}
	if d := requestTimeout(1 << 30); d != forwardMaxTimeout {
		t.Fatalf("timeout not capped: %v", d)
	}
}

func TestTLSVersions_ClampsToTLS12(t *testing.T) {
	cases := map[string][2]uint16{
		"Tls12,Tls13":     {tls.VersionTLS12, tls.VersionTLS13},
		"Tls,Tls11,Tls12": {tls.VersionTLS12, tls.VersionTLS12},
		"Tls11":           {tls.VersionTLS12, tls.VersionTLS13},
		"Tls13":           {tls.VersionTLS13, tls.VersionTLS13},
		"":                {tls.VersionTLS12, tls.VersionTLS13},
	}
	for in, want := range cases {
		minV, maxV := tlsVersions(discard, in)
		if minV != want[0] || maxV != want[1] {
			t.Errorf("tlsVersions(%q) = %x-%x, want %x-%x", in, minV, maxV, want[0], want[1])
		}
	}
}

// Config values starting with '-' would be parsed by certbot as flags.
func TestLetsEncrypt_RejectsFlagLikeConfig(t *testing.T) {
	base := LetsEncryptOptions{Enabled: true, Email: "a@b.com", CertbotPath: filepath.Join(t.TempDir(), "no-certbot"),
		CertificateDirectory: t.TempDir(), ChallengeMethod: "http", WebrootPath: "/var/www"}
	if !NewLetsEncrypt(discard, base, func() {}).safeArgs("smtp.x.com") {
		t.Fatal("valid config rejected")
	}
	for _, mut := range []func(*LetsEncryptOptions){
		func(o *LetsEncryptOptions) { o.Email = "--server=https://evil" },
		func(o *LetsEncryptOptions) { o.CertificateDirectory = "-x" },
		func(o *LetsEncryptOptions) { o.WebrootPath = " --pre-hook=sh" },
	} {
		o := base
		mut(&o)
		if NewLetsEncrypt(discard, o, func() {}).safeArgs("smtp.x.com") {
			t.Errorf("flag-like config accepted: %+v", o)
		}
	}
	if NewLetsEncrypt(discard, base, func() {}).Obtain(context.Background(), "--domains=evil.com") {
		t.Fatal("flag-like domain accepted")
	}
}

func TestMailboxFilter_BlockedSubdomainsAndLeadingDots(t *testing.T) {
	f := NewMailboxFilter(discard, FilterOptions{AllowedDomains: []string{".mailvoid.com"}, BlockedDomains: []string{" .Blocked.com"}}, 100)
	for _, from := range []string{"a@blocked.com", "a@mail.blocked.com", "a@A.B.BLOCKED.COM"} {
		if f.CanAcceptFrom(from, 0) {
			t.Errorf("%s accepted", from)
		}
	}
	if !f.CanAcceptFrom("a@notblocked.com", 0) {
		t.Error("lookalike of a blocked domain rejected")
	}
	if !f.CanDeliverTo("x@mailvoid.com", "a@ok.com") || !f.CanDeliverTo("x@sub.mailvoid.com", "a@ok.com") {
		t.Error("leading-dot allowed domain not normalised")
	}
	if f.CanDeliverTo("x@notmailvoid.com", "a@ok.com") {
		t.Error("lookalike allowed")
	}
}
