package smtpd

import (
	"crypto/tls"
	"crypto/x509"
	"errors"
	"fmt"
	"log/slog"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"

	"software.sslmate.com/src/go-pkcs12"
)

// CertStore holds the active certificate. TLS listeners read it through GetCertificate,
// so a renewed certificate takes effect on the next handshake without a restart.
type CertStore struct {
	log    *slog.Logger
	server ServerOptions
	le     LetsEncryptOptions

	mu   sync.RWMutex
	cert *tls.Certificate
}

func NewCertStore(log *slog.Logger, server ServerOptions, le LetsEncryptOptions) *CertStore {
	return &CertStore{log: log, server: server, le: le}
}

func (s *CertStore) Current() *tls.Certificate {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.cert
}

func (s *CertStore) GetCertificate(*tls.ClientHelloInfo) (*tls.Certificate, error) {
	if c := s.Current(); c != nil {
		return c, nil
	}
	return nil, errors.New("no TLS certificate available")
}

// Reload re-reads the certificate from disk and reports whether one is now available.
// If a certificate is configured but fails to load (unreadable or half-written renewal,
// expired file) the previous certificate stays active, so TLS isn't silently dropped.
func (s *CertStore) Reload() bool {
	cert, failed := s.load()
	s.mu.Lock()
	defer s.mu.Unlock()
	if cert == nil && failed && s.cert != nil && !isExpired(s.cert) {
		s.log.Error("Keeping the previously loaded certificate because the reload failed",
			"notAfter", s.cert.Leaf.NotAfter.UTC())
		return true
	}
	s.cert = cert
	return cert != nil
}

// load returns the configured certificate. failed reports that a certificate source was
// configured but couldn't be used, as opposed to TLS simply not being configured.
func (s *CertStore) load() (cert *tls.Certificate, failed bool) {
	if !s.server.EnableSsl {
		s.log.Info("SSL is disabled in configuration")
		return nil, false
	}

	if d := strings.TrimSpace(s.server.LetsEncryptDomain); d != "" {
		s.log.Info("Attempting to get Let's Encrypt certificate", "domain", d)
		if cert, err := loadLetsEncryptCert(s.le, d); err == nil {
			if isExpired(cert) {
				s.log.Error("Let's Encrypt certificate is expired; refusing to use it", "domain", d, "notAfter", cert.Leaf.NotAfter.UTC())
				failed = true
			} else {
				s.log.Info("Successfully loaded Let's Encrypt certificate", "domain", d, "notAfter", cert.Leaf.NotAfter.UTC())
				return cert, false
			}
		} else {
			s.log.Warn("Let's Encrypt certificate not available", "domain", d, "error", err)
			failed = true
		}
	}

	path := strings.TrimSpace(s.server.CertificatePath)
	if path == "" {
		s.log.Warn("SSL is enabled but no certificate path is configured and Let's Encrypt certificate is not available")
		return nil, failed
	}
	if _, err := os.Stat(path); err != nil {
		s.log.Error("Certificate file not found", "path", path)
		return nil, true
	}

	s.log.Info("Loading certificate", "path", path)
	var err error
	switch strings.ToLower(filepath.Ext(path)) {
	case ".pfx", ".p12":
		cert, err = loadPfx(path, s.server.CertificatePassword)
	case ".pem", ".crt":
		cert, err = loadPem(path)
	default:
		s.log.Error("Unsupported certificate format. Supported formats: .pfx, .p12, .pem, .crt", "extension", filepath.Ext(path))
		return nil, true
	}
	if err != nil {
		s.log.Error("Failed to load certificate", "path", path, "error", err)
		return nil, true
	}

	s.log.Info("Certificate loaded successfully", "subject", cert.Leaf.Subject.String(), "expiresUtc", cert.Leaf.NotAfter.UTC())
	if isExpired(cert) {
		s.log.Error("Certificate has expired; refusing to use it", "expiresUtc", cert.Leaf.NotAfter.UTC())
		return nil, true
	}
	if time.Until(cert.Leaf.NotAfter) < 30*24*time.Hour {
		s.log.Warn("Certificate will expire soon", "expiresUtc", cert.Leaf.NotAfter.UTC())
	}
	return cert, false
}

// loadLetsEncryptCert finds a certbot-issued certificate. The .NET service ran certbot
// with --config-dir {CertificateDirectory}/config, so live certs sit under config/live;
// the standard certbot layout (live/) and the legacy PFX copy are fallbacks.
func loadLetsEncryptCert(le LetsEncryptOptions, domain string) (*tls.Certificate, error) {
	for _, dir := range []string{
		filepath.Join(le.CertificateDirectory, "config", "live", domain),
		filepath.Join(le.CertificateDirectory, "live", domain),
	} {
		certFile, keyFile := filepath.Join(dir, "fullchain.pem"), filepath.Join(dir, "privkey.pem")
		if fileExists(certFile) && fileExists(keyFile) {
			cert, err := tls.LoadX509KeyPair(certFile, keyFile)
			if err != nil {
				return nil, err
			}
			return &cert, nil
		}
	}
	pfx := pfxPath(le, domain)
	if fileExists(pfx) {
		return loadPfx(pfx, le.CertificatePassword)
	}
	return nil, fmt.Errorf("no certificate found under %s", le.CertificateDirectory)
}

func pfxPath(le LetsEncryptOptions, domain string) string {
	return filepath.Join(le.CertificateDirectory, "pfx", domain+".pfx")
}

func loadPfx(path, password string) (*tls.Certificate, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}
	key, leaf, chain, err := pkcs12.DecodeChain(data, password)
	if err != nil {
		return nil, err
	}
	cert := &tls.Certificate{PrivateKey: key, Leaf: leaf, Certificate: [][]byte{leaf.Raw}}
	for _, c := range chain {
		cert.Certificate = append(cert.Certificate, c.Raw)
	}
	return cert, nil
}

// loadPem accepts a certificate with a sibling .key file, or a combined cert+key PEM.
func loadPem(certPath string) (*tls.Certificate, error) {
	keyPath := strings.TrimSuffix(certPath, filepath.Ext(certPath)) + ".key"
	if !fileExists(keyPath) {
		keyPath = certPath
	}
	certPEM, err := os.ReadFile(certPath)
	if err != nil {
		return nil, err
	}
	keyPEM, err := os.ReadFile(keyPath)
	if err != nil {
		return nil, err
	}
	cert, err := tls.X509KeyPair(certPEM, keyPEM)
	if err != nil {
		return nil, err
	}
	return &cert, nil
}

// writePfx keeps the legacy {CertificateDirectory}/pfx/{domain}.pfx copy up to date so
// the volume stays usable if the .NET server is ever started against it again.
func writePfx(le LetsEncryptOptions, domain string, cert *tls.Certificate) error {
	var chain []*x509.Certificate
	for _, der := range cert.Certificate[1:] {
		c, err := x509.ParseCertificate(der)
		if err != nil {
			return err
		}
		chain = append(chain, c)
	}
	data, err := pkcs12.Modern.Encode(cert.PrivateKey, cert.Leaf, chain, le.CertificatePassword)
	if err != nil {
		return err
	}
	path := pfxPath(le, domain)
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return err
	}
	return os.WriteFile(path, data, 0o600)
}

func isExpired(cert *tls.Certificate) bool {
	return cert.Leaf == nil || !cert.Leaf.NotAfter.After(time.Now())
}

func fileExists(path string) bool {
	st, err := os.Stat(path)
	return err == nil && !st.IsDir()
}

// tlsVersions maps the .NET SslProtocols list ("Tls12,Tls13") to a min/max version.
func tlsVersions(log *slog.Logger, protocols string) (minV, maxV uint16) {
	names := map[string]uint16{
		"tls":   tls.VersionTLS10,
		"tls11": tls.VersionTLS11,
		"tls12": tls.VersionTLS12,
		"tls13": tls.VersionTLS13,
	}
	for _, p := range strings.Split(protocols, ",") {
		p = strings.ToLower(strings.TrimSpace(p))
		if p == "" {
			continue
		}
		v, ok := names[p]
		if !ok {
			log.Warn("Unknown TLS protocol", "protocol", p)
			continue
		}
		if minV == 0 || v < minV {
			minV = v
		}
		if v > maxV {
			maxV = v
		}
	}
	if minV == 0 {
		if strings.TrimSpace(protocols) != "" {
			log.Warn("No valid TLS protocols specified. Falling back to TLS 1.2 and 1.3")
		}
		return tls.VersionTLS12, tls.VersionTLS13
	}
	return minV, maxV
}
