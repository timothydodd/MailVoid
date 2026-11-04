package api

import (
	"encoding/json"
	"mailvoid/internal/ordered"
	"strings"
	"testing"
	"time"
)

func TestExtractMail(t *testing.T) {
	payload := `{"From":"\"Jane Doe\" <jane@example.com>","To":"box@kk.mailvoid.com",
		"Headers":{"From":"Jane <jane@example.com>","To":"box@kk.mailvoid.com, Other@kk.mailvoid.com","Cc":"cc@x.org",
			"Subject":"=?utf-8?Q?Caf=C3=A9_time?=","Content-Type":"text/html; charset=utf-8",
			"Authentication-Results":"mx; spf=PASS smtp.mailfrom=x; dkim=fail","Date":"Mon, 10 Aug 2026 18:36:21 +0000 (UTC)"},
		"Html":"<p>hi</p>","Text":"hi",
		"Attachments":[{"Filename":"a.txt","ContentType":"text/plain","Content":"aGVsbG8="}],
		"MessageId":"abc@x"}`
	var d MailData
	if err := json.Unmarshal([]byte(payload), &d); err != nil {
		t.Fatal(err)
	}
	m := extractMail(&d)

	if m.From != "jane@example.com" || m.To != "box@kk.mailvoid.com" {
		t.Errorf("addresses: %q %q", m.From, m.To)
	}
	if deref(m.FromName) != "Jane Doe" {
		t.Errorf("FromName = %q", deref(m.FromName))
	}
	if m.Subject != "Café time" {
		t.Errorf("Subject = %q", m.Subject)
	}
	if !m.IsHTML || m.Text != "<p>hi</p>" {
		t.Errorf("body: html=%v %q", m.IsHTML, m.Text)
	}
	if deref(m.ToOthers) != "Other@kk.mailvoid.com, cc@x.org" {
		t.Errorf("ToOthers = %q", deref(m.ToOthers))
	}
	if deref(m.Charsets) != "utf-8" || deref(m.SpfResult) != "pass" || deref(m.DkimResult) != "fail" {
		t.Errorf("charset/spf/dkim = %q %q %q", deref(m.Charsets), deref(m.SpfResult), deref(m.DkimResult))
	}
	if !strings.HasPrefix(deref(m.Headers), `{"From":`) {
		t.Errorf("headers must keep payload order: %s", deref(m.Headers))
	}
	if deref(m.Attachments) != `[{"filename":"a.txt","contentType":"text/plain","sizeBytes":5,"content":"aGVsbG8="}]` {
		t.Errorf("attachments = %s", deref(m.Attachments))
	}
	// CreatedOn is always the receipt time, never the sender-supplied Date header.
	if time.Since(m.CreatedOn.Time) > time.Minute {
		t.Errorf("CreatedOn = %v", m.CreatedOn)
	}
}

func TestExtractSubdomain(t *testing.T) {
	cases := map[string]string{
		"a@kk.dbmk2.com":   "kk",
		"A@KK.Example.com": "kk",
		"jojo@dbmk2.com":   "dbmk2",
		"":                 "default",
		"nodomain":         "default",
	}
	for in, want := range cases {
		if got := extractSubdomain(in); got != want {
			t.Errorf("extractSubdomain(%q) = %q, want %q", in, got, want)
		}
	}
}

// Internationalized addresses must be kept whole; an ASCII-only pattern used to
// truncate peña.qa5546@dbmk2.com to a.qa5546@dbmk2.com and file it in the wrong mailbox.
func TestExtractEmailAddress_Unicode(t *testing.T) {
	cases := map[string]string{
		"peña.qa5546@dbmk2.com":                   "peña.qa5546@dbmk2.com",
		`"José Peña" <josé.peña@kk.mailvoid.com>`: "josé.peña@kk.mailvoid.com",
		"Ünïcödé <müller@bücher.de>":              "müller@bücher.de",
		"plain@example.com":                       "plain@example.com",
	}
	for in, want := range cases {
		if got := extractEmailAddress(in); got != want {
			t.Errorf("extractEmailAddress(%q) = %q, want %q", in, got, want)
		}
	}

	var h ordered.Map
	h.Set("To", "peña.qa5546@dbmk2.com, zoë@dbmk2.com")
	h.Set("Cc", "renée@dbmk2.com")
	if got := deref(additionalRecipients(h, "peña.qa5546@dbmk2.com")); got != "zoë@dbmk2.com, renée@dbmk2.com" {
		t.Errorf("additionalRecipients = %q", got)
	}
}
