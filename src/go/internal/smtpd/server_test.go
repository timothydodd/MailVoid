package smtpd

import (
	"bufio"
	"context"
	"crypto/tls"
	"net"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
	"time"
)

func freePort(t *testing.T) int {
	t.Helper()
	l, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	defer l.Close()
	return l.Addr().(*net.TCPAddr).Port
}

// ehlo connects (optionally over TLS) and returns the EHLO response lines.
func ehlo(t *testing.T, port int, useTLS bool) string {
	t.Helper()
	addr := "127.0.0.1:" + strconv.Itoa(port)
	var c net.Conn
	var err error
	if useTLS {
		c, err = tls.DialWithDialer(&net.Dialer{Timeout: 2 * time.Second}, "tcp", addr, &tls.Config{InsecureSkipVerify: true})
	} else {
		c, err = net.DialTimeout("tcp", addr, 2*time.Second)
	}
	if err != nil {
		t.Fatalf("dial %d: %v", port, err)
	}
	defer c.Close()
	_ = c.SetDeadline(time.Now().Add(5 * time.Second))
	r := bufio.NewReader(c)
	if _, err := r.ReadString('\n'); err != nil { // greeting
		t.Fatalf("greeting on %d: %v", port, err)
	}
	c.Write([]byte("EHLO test\r\n"))
	var sb strings.Builder
	for {
		line, err := r.ReadString('\n')
		if err != nil {
			t.Fatalf("ehlo on %d: %v", port, err)
		}
		sb.WriteString(line)
		if len(line) >= 4 && line[3] == ' ' {
			return sb.String()
		}
	}
}

// A certificate appearing at runtime enables STARTTLS on Port and starts the TLS ports
// without unbinding the ports that were already serving.
func TestServer_CertificateAppearingEnablesTLS(t *testing.T) {
	certPath := filepath.Join(t.TempDir(), "cert.pem")
	opts := ServerOptions{Port: freePort(t), TestPort: freePort(t), SslPort: freePort(t), StartTlsPort: freePort(t),
		Name: "test", MaxMessageSize: 1 << 20, EnableSsl: true, CertificatePath: certPath, TlsProtocols: "Tls12,Tls13",
		MaxRecipients: 50, MaxConnections: 100, MaxConnectionsPerIp: 10}
	certs := NewCertStore(discard, opts, LetsEncryptOptions{})
	if certs.Reload() {
		t.Fatal("no certificate should exist yet")
	}
	q := newTestQueue(t, t.TempDir(), 3)
	s := NewServer(context.Background(), discard, opts, NewMailboxFilter(discard, FilterOptions{AllowedDomains: []string{"mailvoid.com"}}, 1<<20), q, certs)
	s.Start()

	if strings.Contains(ehlo(t, opts.Port, false), "STARTTLS") {
		t.Fatal("STARTTLS advertised without a certificate")
	}
	if _, err := net.DialTimeout("tcp", "127.0.0.1:"+strconv.Itoa(opts.SslPort), time.Second); err == nil {
		t.Fatal("implicit TLS port should not be listening yet")
	}

	writeSelfSigned(t, certPath)
	s.CertificateChanged()

	if !strings.Contains(ehlo(t, opts.Port, false), "STARTTLS") {
		t.Fatal("STARTTLS not advertised after the certificate appeared")
	}
	if !strings.Contains(ehlo(t, opts.StartTlsPort, false), "STARTTLS") {
		t.Fatal("StartTlsPort not serving")
	}
	ehlo(t, opts.SslPort, true) // implicit TLS handshake + EHLO
	if strings.Contains(ehlo(t, opts.TestPort, false), "STARTTLS") {
		t.Fatal("the test port stays plain text")
	}

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	s.Stop(ctx)
	// A certificate event after Stop must not bring listeners back.
	s.CertificateChanged()
	if _, err := net.DialTimeout("tcp", "127.0.0.1:"+strconv.Itoa(opts.Port), time.Second); err == nil {
		t.Fatal("listener restarted after Stop")
	}
}
