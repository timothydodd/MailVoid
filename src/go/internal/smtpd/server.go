package smtpd

import (
	"context"
	"crypto/tls"
	"errors"
	"fmt"
	"io"
	"log/slog"
	"net"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/emersion/go-smtp"
)

// Server owns the SMTP listeners: Port (plain, STARTTLS when a certificate is loaded),
// TestPort (plain only) and, with TLS available, SslPort (implicit TLS) and StartTlsPort.
// It never relays or sends mail and never accepts AUTH.
type Server struct {
	log    *slog.Logger
	opts   ServerOptions
	filter *MailboxFilter
	queue  *Queue
	certs  *CertStore
	// ctx bounds Enqueue so a full queue cannot block shutdown forever.
	ctx context.Context

	mu        sync.Mutex
	servers   []*smtp.Server
	tlsActive bool
}

func NewServer(ctx context.Context, log *slog.Logger, opts ServerOptions, filter *MailboxFilter, queue *Queue, certs *CertStore) *Server {
	return &Server{ctx: ctx, log: log, opts: opts, filter: filter, queue: queue, certs: certs}
}

func (s *Server) Start() {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.startLocked()
}

func (s *Server) startLocked() {
	cert := s.certs.Current()
	sslEnabled := s.opts.EnableSsl && cert != nil
	s.log.Info("SMTP Server Configuration", "enableSsl", s.opts.EnableSsl, "certificateAvailable", cert != nil)
	if cert != nil && cert.Leaf != nil {
		s.log.Info("Certificate Details", "subject", cert.Leaf.Subject.String(), "expires", cert.Leaf.NotAfter.UTC())
	}

	var tlsConfig *tls.Config
	if sslEnabled {
		minV, maxV := tlsVersions(s.log, s.opts.TlsProtocols)
		tlsConfig = &tls.Config{GetCertificate: s.certs.GetCertificate, MinVersion: minV, MaxVersion: maxV}
	}

	s.listen(s.opts.Port, tlsConfig, false)
	if s.opts.TestPort != s.opts.Port {
		s.listen(s.opts.TestPort, nil, false)
	}
	if sslEnabled {
		s.listen(s.opts.SslPort, tlsConfig, true)
		s.listen(s.opts.StartTlsPort, tlsConfig, false)
		s.log.Info("SSL/TLS enabled", "protocols", s.opts.TlsProtocols)
	} else if s.opts.EnableSsl {
		s.log.Warn("SSL is enabled in configuration but no valid certificate was loaded. SSL endpoints will not be available.")
	}
	s.tlsActive = sslEnabled
}

// listen starts one endpoint. A port that cannot be bound (e.g. 25 without
// CAP_NET_BIND_SERVICE) is logged and skipped so the remaining endpoints still serve.
func (s *Server) listen(port int, tlsConfig *tls.Config, implicitTLS bool) {
	mode := "plain text"
	switch {
	case implicitTLS:
		mode = "implicit SSL/TLS"
	case tlsConfig != nil:
		mode = "STARTTLS"
	}

	ln, err := net.Listen("tcp", ":"+strconv.Itoa(port))
	if err != nil {
		s.log.Error("SMTP server error: failed to bind port", "port", port, "mode", mode, "error", err)
		return
	}
	if implicitTLS {
		ln = tls.NewListener(ln, tlsConfig)
	}

	srv := smtp.NewServer(smtp.BackendFunc(func(c *smtp.Conn) (smtp.Session, error) {
		return s.newSession(c, port), nil
	}))
	srv.Domain = s.opts.Name
	srv.MaxMessageBytes = int64(s.opts.MaxMessageSize)
	srv.ReadTimeout = 5 * time.Minute
	srv.WriteTimeout = time.Minute
	srv.TLSConfig = tlsConfig
	srv.ErrorLog = slogAdapter{s.log}

	s.servers = append(s.servers, srv)
	s.log.Info("SMTP endpoint listening", "port", port, "mode", mode)
	go func() {
		if err := srv.Serve(ln); err != nil && !errors.Is(err, smtp.ErrServerClosed) {
			s.log.Error("SMTP server error", "port", port, "error", err)
		}
	}()
}

// Stop closes the listeners and waits for in-flight sessions (mid-DATA) to drain.
func (s *Server) Stop(ctx context.Context) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.stopLocked(ctx)
}

func (s *Server) stopLocked(ctx context.Context) {
	s.log.Info("Stopping SMTP server")
	var wg sync.WaitGroup
	for _, srv := range s.servers {
		wg.Add(1)
		go func(srv *smtp.Server) {
			defer wg.Done()
			if err := srv.Shutdown(ctx); err != nil {
				s.log.Warn("SMTP server shutdown timed out before all sessions drained")
				srv.Close()
			}
		}(srv)
	}
	wg.Wait()
	s.servers = nil
	s.log.Info("SMTP server stopped")
}

// CertificateChanged reloads the certificate. Running TLS listeners pick it up on the
// next handshake; if TLS was unavailable at startup the listeners are restarted so the
// STARTTLS and implicit-TLS endpoints come up.
func (s *Server) CertificateChanged() {
	available := s.certs.Reload()
	s.mu.Lock()
	defer s.mu.Unlock()
	if available == s.tlsActive {
		s.log.Info("TLS certificate reloaded")
		return
	}
	s.log.Info("Restarting SMTP server to apply certificate change")
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()
	s.stopLocked(ctx)
	time.Sleep(2 * time.Second)
	s.startLocked()
	s.log.Info("SMTP server restarted successfully")
}

type session struct {
	s      *Server
	conn   *smtp.Conn
	port   int
	remote string
	from   string
	rcpts  []string
}

func (s *Server) newSession(c *smtp.Conn, port int) *session {
	remote := "unknown"
	if addr := c.Conn().RemoteAddr(); addr != nil {
		remote = addr.String()
	}
	s.log.Info("SMTP session started", "remote", remote, "port", port, "security", securityInfo(c))
	return &session{s: s, conn: c, port: port, remote: remote}
}

func (ss *session) Mail(from string, opts *smtp.MailOptions) error {
	var size int64
	if opts != nil {
		size = opts.Size
	}
	if !ss.s.filter.CanAcceptFrom(from, size) {
		return &smtp.SMTPError{Code: 553, EnhancedCode: smtp.EnhancedCode{5, 7, 1}, Message: "Mailbox name not allowed"}
	}
	ss.from = from
	return nil
}

func (ss *session) Rcpt(to string, _ *smtp.RcptOptions) error {
	if !ss.s.filter.CanDeliverTo(to, ss.from) {
		return &smtp.SMTPError{Code: 550, EnhancedCode: smtp.EnhancedCode{5, 1, 1}, Message: "Mailbox unavailable"}
	}
	ss.rcpts = append(ss.rcpts, to)
	return nil
}

func (ss *session) Data(r io.Reader) error {
	raw, err := io.ReadAll(r)
	if err != nil {
		return err // includes go-smtp's ErrDataTooLarge
	}
	log := ss.s.log
	security := securityInfo(ss.conn)

	// Envelope recipients are authoritative: BCC recipients never appear in the headers.
	recipients := distinctFold(ss.rcpts)

	data, perr := BuildEmailData(raw, ss.from, recipients)
	if perr != nil {
		log.Warn("Failed to parse MIME message; forwarding raw content with parse-failed marker", "error", perr)
	} else {
		log.Info("Received message", "from", data.From, "to", strings.Join(recipients, ", "), "subject", data.Subject, "security", security)
	}

	if len(data.To) == 0 {
		log.Warn("No envelope recipients for message; rejecting", "from", data.From)
		return &smtp.SMTPError{Code: 550, EnhancedCode: smtp.EnhancedCode{5, 1, 1}, Message: "No valid recipients"}
	}

	id, err := ss.s.queue.Enqueue(ss.s.ctx, data)
	if err != nil {
		log.Error("Error processing email message", "error", err)
		return &smtp.SMTPError{Code: 554, EnhancedCode: smtp.EnhancedCode{5, 3, 0}, Message: "Error processing message"}
	}
	messageID := ""
	if data.MessageId != nil {
		messageID = *data.MessageId
	}
	log.Info("✓ Email queued", "messageId", messageID, "queueId", id, "from", data.From, "recipients", len(data.To), "security", security)
	return nil
}

func (ss *session) Reset() {
	ss.from = ""
	ss.rcpts = nil
}

func (ss *session) Logout() error {
	ss.s.log.Info("SMTP session completed", "remote", ss.remote, "port", ss.port, "security", securityInfo(ss.conn))
	return nil
}

func securityInfo(c *smtp.Conn) string {
	if state, ok := c.TLSConnectionState(); ok {
		return "SSL/TLS " + tls.VersionName(state.Version)
	}
	return "Plain Text"
}

func distinctFold(values []string) []string {
	out := []string{}
	seen := map[string]bool{}
	for _, v := range values {
		if strings.TrimSpace(v) == "" {
			continue
		}
		k := strings.ToLower(v)
		if !seen[k] {
			seen[k] = true
			out = append(out, v)
		}
	}
	return out
}

// slogAdapter routes go-smtp's internal errors into the structured logger.
type slogAdapter struct{ log *slog.Logger }

func (a slogAdapter) Printf(format string, v ...any) {
	a.log.Warn("smtp: " + fmt.Sprintf(format, v...))
}
func (a slogAdapter) Println(v ...any) { a.log.Warn("smtp: " + strings.TrimSpace(fmt.Sprintln(v...))) }
