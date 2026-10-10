package api

import (
	"encoding/json"
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
	// A trailing "(UTC)" comment defeats parsing (as in .NET), so receipt time is used.
	if time.Since(m.CreatedOn.Time) > time.Minute {
		t.Errorf("CreatedOn = %v", m.CreatedOn)
	}
}

func TestParseMailDate(t *testing.T) {
	recent := time.Now().UTC().Add(-time.Hour).Truncate(time.Second)
	in := recent.Format("Mon, 2 Jan 2006 15:04:05 -0700")
	got, ok := parseMailDate(in)
	if !ok || !got.Equal(recent) {
		t.Errorf("parse %q = %v %v", in, got, ok)
	}
	if _, ok := parseMailDate(time.Now().AddDate(0, 0, 3).Format("2006-01-02 15:04:05")); ok {
		t.Error("future dates beyond a day must be rejected")
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
