package smtpd

import (
	"context"
	"log/slog"
	"strings"
)

// MailboxFilter keeps the server from acting as an open relay: it only accepts mail
// addressed to the configured test domains.
type MailboxFilter struct {
	log            *slog.Logger
	opts           FilterOptions
	maxMessageSize int
}

func NewMailboxFilter(log *slog.Logger, opts FilterOptions, maxMessageSize int) *MailboxFilter {
	return &MailboxFilter{log: log, opts: opts, maxMessageSize: maxMessageSize}
}

func (f *MailboxFilter) CanAcceptFrom(from string, size int64) bool {
	f.log.Debug("Checking if can accept email", "from", from, "size", size)

	// The null reverse-path (MAIL FROM:<>) is how bounces and DSNs are sent (RFC 5321
	// 4.5.5); accept it so they can be captured. Recipient checks still apply.
	if strings.TrimSpace(from) == "" {
		if size > int64(f.maxMessageSize) {
			f.log.Warn("Rejected email exceeding size limit", "size", size, "max", f.maxMessageSize)
			return false
		}
		f.log.Info("Accepted email with null reverse-path (bounce/DSN)")
		return true
	}

	// Blocked domains match subdomains too, so blocking "example.com" also blocks
	// "mail.example.com".
	domain := GetDomainFromEmail(from)
	if matchesDomain(domain, f.opts.BlockedDomains) {
		f.log.Warn("Rejected email from blocked domain", "domain", domain)
		return false
	}

	if !IsValidEmailFormat(from) {
		f.log.Warn("Rejected email with invalid format", "email", from)
		return false
	}

	// Advisory MAIL FROM SIZE check; go-smtp enforces the hard limit during DATA.
	if size > int64(f.maxMessageSize) {
		f.log.Warn("Rejected email exceeding size limit", "size", size, "max", f.maxMessageSize)
		return false
	}

	f.log.Info("Accepted email", "from", from)
	return true
}

func (f *MailboxFilter) CanDeliverTo(to, from string) bool {
	return f.CanDeliverToLevel(to, from, slog.LevelWarn)
}

// CanDeliverToLevel is CanDeliverTo with rejections logged at rejectLevel, so a session
// can demote repeated rejections (e.g. a dictionary attack) to Debug.
func (f *MailboxFilter) CanDeliverToLevel(to, from string, rejectLevel slog.Level) bool {
	f.log.Debug("Checking if can deliver", "to", to, "from", from)
	reject := func(msg string, args ...any) bool {
		f.log.Log(context.Background(), rejectLevel, msg, args...)
		return false
	}

	if strings.TrimSpace(to) == "" {
		return reject("Rejected delivery to empty recipient address")
	}
	if !IsValidEmailFormat(to) {
		return reject("Rejected delivery to invalid email format", "email", to)
	}

	toDomain := GetDomainFromEmail(to)
	if !IsAllowedDomain(toDomain, f.opts.AllowedDomains) {
		return reject("Rejected delivery to non-test domain", "domain", toDomain)
	}

	if strings.EqualFold(from, to) {
		return reject("Rejected email loop attempt", "email", to)
	}

	f.log.Info("Accepted delivery", "to", to)
	return true
}

func GetDomainFromEmail(email string) string {
	if i := strings.LastIndex(email, "@"); i >= 0 {
		return email[i+1:]
	}
	return ""
}

// IsAllowedDomain matches an allowed domain exactly or as a parent domain, so
// "sub.mailvoid.com" matches "mailvoid.com" but the lookalike "notmailvoid.com" does not.
func IsAllowedDomain(domain string, allowed []string) bool {
	return matchesDomain(domain, allowed)
}

// matchesDomain reports whether domain equals, or is a subdomain of, any list entry.
// Entries are trimmed and a leading dot (".mailvoid.com") is ignored.
func matchesDomain(domain string, list []string) bool {
	if strings.TrimSpace(domain) == "" {
		return false
	}
	lower := strings.ToLower(domain)
	for _, a := range list {
		la := strings.ToLower(strings.TrimPrefix(strings.TrimSpace(a), "."))
		if la == "" {
			continue
		}
		if lower == la || strings.HasSuffix(lower, "."+la) {
			return true
		}
	}
	return false
}

func IsValidEmailFormat(email string) bool {
	if strings.TrimSpace(email) == "" {
		return false
	}
	at := strings.Index(email, "@")
	if at <= 0 || at == len(email)-1 {
		return false
	}
	if strings.Count(email, "@") != 1 {
		return false
	}
	if !strings.Contains(email[at+1:], ".") {
		return false
	}
	return !strings.ContainsAny(email, " \t\n\r()[]\\,;:<>")
}
