package smtpd

import (
	"go/parser"
	"go/token"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

// TestReceiveOnly guards the server's purpose: it accepts mail and hands it to the
// MailVoid API over HTTP, and must never be able to send or relay mail itself.
func TestReceiveOnly(t *testing.T) {
	forbiddenImports := []string{"net/smtp"}
	forbiddenCalls := []string{"smtp.Dial", "smtp.NewClient", "smtp.SendMail", "SendMail(", "LookupMX", "net.Dial", "tls.Dial"}

	files, _ := filepath.Glob("*.go")
	files = append(files, "../../cmd/mailvoid-smtp/main.go")
	for _, f := range files {
		if strings.HasSuffix(f, "_test.go") {
			continue
		}
		src, err := os.ReadFile(f)
		if err != nil {
			t.Fatal(err)
		}
		parsed, err := parser.ParseFile(token.NewFileSet(), f, src, parser.ImportsOnly)
		if err != nil {
			t.Fatal(err)
		}
		for _, imp := range parsed.Imports {
			for _, bad := range forbiddenImports {
				if strings.Trim(imp.Path.Value, `"`) == bad {
					t.Errorf("%s imports %s: the SMTP server must not send mail", f, bad)
				}
			}
		}
		for _, bad := range forbiddenCalls {
			if strings.Contains(string(src), bad) {
				t.Errorf("%s uses %s: the SMTP server must not send mail", f, bad)
			}
		}
	}
}

func TestRelayToUnlistedDomainRejected(t *testing.T) {
	f := NewMailboxFilter(discard, FilterOptions{AllowedDomains: []string{"mailvoid.com"}}, 1<<20)
	for _, to := range []string{"victim@gmail.com", "x@notmailvoid.com", "x@mailvoid.com.evil.org"} {
		if f.CanDeliverTo(to, "sender@example.org") {
			t.Errorf("relay to %s must be rejected", to)
		}
	}
	if !f.CanDeliverTo("box@sub.mailvoid.com", "sender@example.org") {
		t.Error("mail for the configured domain should be accepted")
	}
	empty := NewMailboxFilter(discard, FilterOptions{}, 1<<20)
	if empty.CanDeliverTo("box@mailvoid.com", "sender@example.org") {
		t.Error("with no AllowedDomains configured, every recipient must be rejected")
	}
}
