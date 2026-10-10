package smtpd

import (
	"bytes"
	"context"
	"fmt"
	"log/slog"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"time"
)

// LetsEncrypt obtains and renews certificates by shelling out to certbot with the same
// arguments as the .NET service, so an existing /etc/letsencrypt volume (account,
// renewal config, cloudflare.ini) keeps working.
type LetsEncrypt struct {
	log  *slog.Logger
	opts LetsEncryptOptions
	// onCertChanged is invoked after a certificate is obtained or renewed.
	onCertChanged func()
}

func NewLetsEncrypt(log *slog.Logger, opts LetsEncryptOptions, onCertChanged func()) *LetsEncrypt {
	return &LetsEncrypt{log: log, opts: opts, onCertChanged: onCertChanged}
}

// Run performs the initial certificate setup and then checks for renewal every
// RenewalCheckIntervalHours. It runs in the background so a slow DNS-01 challenge never
// blocks startup.
func (l *LetsEncrypt) Run(ctx context.Context) {
	if !l.opts.Enabled || len(l.opts.Domains) == 0 {
		l.log.Info("Let's Encrypt service is disabled or has no domains configured")
		return
	}
	l.log.Info("Let's Encrypt service starting", "domains", strings.Join(l.opts.Domains, ", "))

	l.initialSetup(ctx)

	interval := time.Duration(max(1, l.opts.RenewalCheckIntervalHours)) * time.Hour
	l.log.Info("Let's Encrypt renewal service started", "checkEveryHours", l.opts.RenewalCheckIntervalHours)
	for {
		select {
		case <-ctx.Done():
			l.log.Info("Let's Encrypt renewal service is stopping")
			return
		case <-time.After(interval):
		}
		l.checkAndRenew(ctx)
	}
}

func (l *LetsEncrypt) initialSetup(ctx context.Context) {
	l.log.Info("Performing initial certificate setup")
	for _, domain := range l.opts.Domains {
		if l.isValid(domain) {
			exp, _ := l.expiration(domain)
			l.log.Info("Certificate is valid", "domain", domain, "expires", exp)
			continue
		}
		l.log.Info("Certificate is not valid or missing. Obtaining new certificate", "domain", domain)
		if l.Obtain(ctx, domain) {
			l.log.Info("Successfully obtained certificate", "domain", domain)
			l.onCertChanged()
		} else {
			l.log.Error("Failed to obtain certificate", "domain", domain)
		}
	}
}

func (l *LetsEncrypt) checkAndRenew(ctx context.Context) {
	l.log.Debug("Checking certificates for renewal")
	renewed := false
	for _, domain := range l.opts.Domains {
		exp, ok := l.expiration(domain)
		if !ok {
			l.log.Warn("Could not determine expiration date for certificate", "domain", domain)
			if l.Obtain(ctx, domain) {
				l.log.Info("Successfully obtained new certificate", "domain", domain)
				renewed = true
			}
			continue
		}
		days := time.Until(exp).Hours() / 24
		l.log.Debug("Certificate expiry", "domain", domain, "days", fmt.Sprintf("%.1f", days))
		if days > float64(l.opts.RenewalDaysBeforeExpiry) {
			continue
		}
		l.log.Info("Certificate expiring soon. Attempting renewal", "domain", domain, "days", fmt.Sprintf("%.1f", days))
		if l.Renew(ctx, domain) {
			l.log.Info("Successfully renewed certificate", "domain", domain)
			renewed = true
		} else {
			l.log.Error("Failed to renew certificate", "domain", domain)
		}
	}
	if renewed {
		l.log.Info("Certificates were renewed. Reloading SMTP TLS certificate")
		l.onCertChanged()
	}
}

func (l *LetsEncrypt) Obtain(ctx context.Context, domain string) bool {
	if !l.opts.Enabled {
		l.log.Info("Let's Encrypt is disabled in configuration")
		return false
	}
	if strings.TrimSpace(domain) == "" {
		l.log.Error("Domain is required to obtain certificate")
		return false
	}
	l.log.Info("Obtaining Let's Encrypt certificate", "domain", domain)
	if l.isValid(domain) {
		l.log.Info("Valid certificate already exists", "domain", domain)
		return true
	}
	if err := os.MkdirAll(filepath.Join(l.opts.CertificateDirectory, domain), 0o755); err != nil {
		l.log.Error("Error obtaining certificate", "domain", domain, "error", err)
		return false
	}
	if l.isCloudflare() && !l.ensureCloudflareCredentials() {
		return false
	}
	if !l.runCertbot(ctx, l.certbotArgs(domain, false)) {
		l.log.Error("Failed to obtain certificate", "domain", domain)
		return false
	}
	l.refreshPfx(domain)
	return true
}

func (l *LetsEncrypt) Renew(ctx context.Context, domain string) bool {
	if !l.opts.Enabled {
		l.log.Info("Let's Encrypt is disabled in configuration")
		return false
	}
	l.log.Info("Renewing Let's Encrypt certificate", "domain", domain)
	if l.isCloudflare() && !l.ensureCloudflareCredentials() {
		return false
	}
	if !l.runCertbot(ctx, l.certbotArgs(domain, true)) {
		l.log.Warn("Certificate renewal was not needed or failed", "domain", domain)
		return false
	}
	l.refreshPfx(domain)
	return true
}

func (l *LetsEncrypt) isValid(domain string) bool {
	cert, err := loadLetsEncryptCert(l.opts, domain)
	if err != nil {
		return false
	}
	now := time.Now()
	return !cert.Leaf.NotBefore.After(now) &&
		cert.Leaf.NotAfter.After(now.AddDate(0, 0, l.opts.RenewalDaysBeforeExpiry))
}

func (l *LetsEncrypt) expiration(domain string) (time.Time, bool) {
	cert, err := loadLetsEncryptCert(l.opts, domain)
	if err != nil {
		return time.Time{}, false
	}
	return cert.Leaf.NotAfter.UTC(), true
}

// refreshPfx replaces the .NET service's openssl PEM->PFX conversion.
func (l *LetsEncrypt) refreshPfx(domain string) {
	cert, err := loadLetsEncryptCert(l.opts, domain)
	if err != nil {
		l.log.Warn("Certificate or key file not found", "domain", domain, "error", err)
		return
	}
	if err := writePfx(l.opts, domain, cert); err != nil {
		l.log.Error("PFX conversion failed", "domain", domain, "error", err)
		return
	}
	l.log.Info("Successfully converted certificate to PFX format", "domain", domain)
}

func (l *LetsEncrypt) certbotArgs(domain string, renewal bool) []string {
	var args []string
	if renewal {
		args = []string{"renew", "--quiet", "--cert-name", domain}
	} else {
		args = []string{"certonly", "--non-interactive", "--agree-tos", "--email", l.opts.Email}
		switch strings.ToLower(l.opts.ChallengeMethod) {
		case "http":
			args = append(args, "--standalone")
			if l.opts.HttpPort != nil {
				args = append(args, "--http-01-port", strconv.Itoa(*l.opts.HttpPort))
			}
		case "dns":
			args = append(args, "--manual", "--preferred-challenges", "dns")
		case "dns-cloudflare":
			args = append(args, "--dns-cloudflare", "--dns-cloudflare-credentials", l.cloudflarePath())
		case "webroot":
			args = append(args, "--webroot", "--webroot-path", l.opts.WebrootPath)
		default:
			args = append(args, "--standalone")
		}
		args = append(args, "--domains", domain)
	}
	// Renewal also needs the custom directories, otherwise certbot looks in its default
	// config dir and cannot find the lineage that certonly created.
	if strings.TrimSpace(l.opts.CertificateDirectory) != "" {
		args = append(args,
			"--work-dir", l.opts.CertificateDirectory+"/work",
			"--config-dir", l.opts.CertificateDirectory+"/config",
			"--logs-dir", l.opts.CertificateDirectory+"/logs")
	}
	return args
}

func (l *LetsEncrypt) runCertbot(ctx context.Context, args []string) bool {
	l.log.Debug("Running certbot", "arguments", strings.Join(args, " "))
	cmd := exec.CommandContext(ctx, l.opts.CertbotPath, args...)
	var stdout, stderr bytes.Buffer
	cmd.Stdout, cmd.Stderr = &stdout, &stderr
	if err := cmd.Run(); err != nil {
		l.log.Error("Certbot failed", "error", err)
		if s := strings.TrimSpace(stderr.String()); s != "" {
			l.log.Error("Certbot error", "stderr", s)
		}
		if s := strings.TrimSpace(stdout.String()); s != "" {
			l.log.Debug("Certbot output", "stdout", s)
		}
		return false
	}
	l.log.Info("Certbot completed successfully")
	if s := strings.TrimSpace(stdout.String()); s != "" {
		l.log.Debug("Certbot output", "stdout", s)
	}
	return true
}

func (l *LetsEncrypt) isCloudflare() bool {
	return strings.EqualFold(l.opts.ChallengeMethod, "dns-cloudflare")
}

func (l *LetsEncrypt) cloudflarePath() string {
	return filepath.Join(l.opts.CertificateDirectory, "cloudflare.ini")
}

// ensureCloudflareCredentials writes cloudflare.ini owner-only; certbot refuses to use
// a world-readable credentials file.
func (l *LetsEncrypt) ensureCloudflareCredentials() bool {
	if strings.TrimSpace(l.opts.CloudflareApiToken) == "" {
		l.log.Error("dns-cloudflare challenge selected but CloudflareApiToken is not configured")
		return false
	}
	path := l.cloudflarePath()
	content := "# Cloudflare API credentials for certbot-dns-cloudflare\n" +
		"dns_cloudflare_api_token = " + l.opts.CloudflareApiToken + "\n"
	err := os.MkdirAll(filepath.Dir(path), 0o755)
	if err == nil {
		err = os.WriteFile(path, []byte(content), 0o600)
	}
	if err == nil {
		err = os.Chmod(path, 0o600) // tighten if it already existed
	}
	if err != nil {
		l.log.Error("Error writing Cloudflare credentials file", "error", err)
		return false
	}
	l.log.Debug("Wrote Cloudflare credentials file", "path", path)
	return true
}
