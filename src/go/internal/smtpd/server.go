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
	"sync/atomic"
	"time"

	"github.com/emersion/go-smtp"
)

// Server owns the SMTP listeners: Port (plain, STARTTLS when a certificate is loaded),
// TestPort (plain only) and, with TLS available, SslPort (implicit TLS) and StartTlsPort.
// It never relays or sends mail and never accepts AUTH.
//
// Each port has one long-lived TCP listener whose accept loop applies the connection
// limits and hands connections to the port's current go-smtp server. When TLS becomes
// available the go-smtp server behind a port is swapped (old sessions drain in the
// background), so ports are never unbound while the process runs.
type Server struct {
	log     *slog.Logger
	opts    ServerOptions
	filter  *MailboxFilter
	queue   *Queue
	certs   *CertStore
	limiter *connLimiter
	// ctx bounds Enqueue so a full queue cannot block shutdown forever.
	ctx context.Context

	mu        sync.Mutex
	endpoints map[int]*endpoint
	tlsActive bool
	stopped   bool
}

func NewServer(ctx context.Context, log *slog.Logger, opts ServerOptions, filter *MailboxFilter, queue *Queue, certs *CertStore) *Server {
	return &Server{ctx: ctx, log: log, opts: opts, filter: filter, queue: queue, certs: certs,
		limiter: newConnLimiter(log, opts.MaxConnections, opts.MaxConnectionsPerIp), endpoints: map[int]*endpoint{}}
}

func (s *Server) Start() {
	s.mu.Lock()
	defer s.mu.Unlock()

	cert := s.certs.Current()
	sslEnabled := s.opts.EnableSsl && cert != nil
	s.log.Info("SMTP Server Configuration", "enableSsl", s.opts.EnableSsl, "certificateAvailable", cert != nil,
		"maxRecipients", s.opts.MaxRecipients, "maxConnections", s.opts.MaxConnections, "maxConnectionsPerIp", s.opts.MaxConnectionsPerIp)
	if cert != nil && cert.Leaf != nil {
		s.log.Info("Certificate Details", "subject", cert.Leaf.Subject.String(), "expires", cert.Leaf.NotAfter.UTC())
	}
	tlsConfig := s.tlsConfig(sslEnabled)

	s.listen(s.opts.Port, tlsConfig, false)
	if s.opts.TestPort != s.opts.Port {
		s.listen(s.opts.TestPort, nil, false)
	}
	if sslEnabled {
		s.startTLSEndpoints(tlsConfig)
	} else if s.opts.EnableSsl {
		s.log.Warn("SSL is enabled in configuration but no valid certificate was loaded. SSL endpoints will not be available.")
	}
	s.tlsActive = sslEnabled
}

func (s *Server) tlsConfig(enabled bool) *tls.Config {
	if !enabled {
		return nil
	}
	minV, maxV := tlsVersions(s.log, s.opts.TlsProtocols)
	return &tls.Config{GetCertificate: s.certs.GetCertificate, MinVersion: minV, MaxVersion: maxV}
}

func (s *Server) startTLSEndpoints(tlsConfig *tls.Config) {
	s.listen(s.opts.SslPort, tlsConfig, true)
	s.listen(s.opts.StartTlsPort, tlsConfig, false)
	s.log.Info("SSL/TLS enabled", "protocols", s.opts.TlsProtocols)
}

// listen starts one endpoint. A port that cannot be bound (e.g. 25 without
// CAP_NET_BIND_SERVICE) is logged and skipped so the remaining endpoints still serve.
func (s *Server) listen(port int, tlsConfig *tls.Config, implicitTLS bool) {
	if _, exists := s.endpoints[port]; exists {
		return
	}
	ln, err := net.Listen("tcp", ":"+strconv.Itoa(port))
	if err != nil {
		s.log.Error("SMTP server error: failed to bind port", "port", port, "mode", endpointMode(tlsConfig, implicitTLS), "error", err)
		return
	}
	ep := &endpoint{s: s, port: port, ln: ln, implicitTLS: implicitTLS, tlsConfig: tlsConfig}
	ep.cur.Store(s.newBackend(port, tlsConfig, implicitTLS, ln.Addr()))
	s.endpoints[port] = ep
	s.log.Info("SMTP endpoint listening", "port", port, "mode", endpointMode(tlsConfig, implicitTLS))
	go ep.acceptLoop()
}

func endpointMode(tlsConfig *tls.Config, implicitTLS bool) string {
	switch {
	case implicitTLS:
		return "implicit SSL/TLS"
	case tlsConfig != nil:
		return "STARTTLS"
	}
	return "plain text"
}

// newBackend creates a go-smtp server fed by a channel listener. For implicit TLS the
// accept loop wraps connections itself, so the server only gets STARTTLS configs.
func (s *Server) newBackend(port int, tlsConfig *tls.Config, implicitTLS bool, addr net.Addr) *backend {
	srv := smtp.NewServer(smtp.BackendFunc(func(c *smtp.Conn) (smtp.Session, error) {
		return s.newSession(c, port), nil
	}))
	srv.Domain = s.opts.Name
	srv.MaxMessageBytes = int64(s.opts.MaxMessageSize)
	srv.MaxRecipients = max(0, s.opts.MaxRecipients)
	srv.ReadTimeout = 5 * time.Minute
	srv.WriteTimeout = time.Minute
	if !implicitTLS {
		srv.TLSConfig = tlsConfig
	}
	srv.ErrorLog = slogAdapter{s.log}

	b := &backend{srv: srv, conns: newChanListener(addr)}
	go func() {
		if err := srv.Serve(b.conns); err != nil && !errors.Is(err, smtp.ErrServerClosed) && !errors.Is(err, net.ErrClosed) {
			s.log.Error("SMTP server error", "port", port, "error", err)
		}
	}()
	return b
}

// Stop closes the listeners and waits for in-flight sessions (mid-DATA) to drain.
func (s *Server) Stop(ctx context.Context) {
	s.mu.Lock()
	defer s.mu.Unlock()
	s.stopped = true
	s.log.Info("Stopping SMTP server")
	var wg sync.WaitGroup
	for port, ep := range s.endpoints {
		ep.ln.Close()
		wg.Add(1)
		go func(b *backend) {
			defer wg.Done()
			b.shutdown(ctx, s.log)
		}(ep.cur.Load())
		delete(s.endpoints, port)
	}
	wg.Wait()
	s.log.Info("SMTP server stopped")
}

// CertificateChanged reloads the certificate. Running TLS listeners pick it up on the
// next handshake. If TLS availability flips, Port's go-smtp server is swapped to add or
// remove STARTTLS and the SslPort/StartTlsPort endpoints are started or stopped; other
// ports keep serving throughout.
func (s *Server) CertificateChanged() {
	available := s.certs.Reload() && s.opts.EnableSsl
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.stopped {
		return
	}
	if available == s.tlsActive {
		s.log.Info("TLS certificate reloaded")
		return
	}

	tlsConfig := s.tlsConfig(available)
	if ep := s.endpoints[s.opts.Port]; ep != nil {
		old := ep.cur.Swap(s.newBackend(ep.port, tlsConfig, false, ep.ln.Addr()))
		go s.drain(old)
	}
	if available {
		s.startTLSEndpoints(tlsConfig)
	} else {
		for _, port := range []int{s.opts.SslPort, s.opts.StartTlsPort} {
			if ep := s.endpoints[port]; ep != nil && port != s.opts.Port && port != s.opts.TestPort {
				ep.ln.Close()
				go s.drain(ep.cur.Load())
				delete(s.endpoints, port)
			}
		}
		s.log.Warn("TLS certificate no longer available; SSL endpoints stopped")
	}
	s.tlsActive = available
	s.log.Info("SMTP endpoints updated for certificate change", "tls", available)
}

// drain lets a replaced server finish its sessions (bounded by the session timeouts).
func (s *Server) drain(b *backend) {
	ctx, cancel := context.WithTimeout(context.Background(), 6*time.Minute)
	defer cancel()
	b.shutdown(ctx, s.log)
}

type endpoint struct {
	s           *Server
	port        int
	ln          net.Listener
	implicitTLS bool
	tlsConfig   *tls.Config
	cur         atomic.Pointer[backend]
}

func (ep *endpoint) acceptLoop() {
	for {
		c, err := ep.ln.Accept()
		if err != nil {
			if errors.Is(err, net.ErrClosed) {
				return
			}
			ep.s.log.Warn("SMTP accept error", "port", ep.port, "error", err)
			time.Sleep(100 * time.Millisecond)
			continue
		}
		lc, ok := ep.s.limiter.acquire(c, ep.port)
		if !ok {
			if !ep.implicitTLS {
				_ = c.SetWriteDeadline(time.Now().Add(5 * time.Second))
				_, _ = c.Write([]byte("421 4.7.0 Too many connections, try again later\r\n"))
			}
			c.Close()
			continue
		}
		var conn net.Conn = lc
		if ep.implicitTLS {
			conn = tls.Server(lc, ep.tlsConfig)
		}
		if !ep.cur.Load().conns.deliver(conn) {
			conn.Close()
		}
	}
}

type backend struct {
	srv   *smtp.Server
	conns *chanListener
}

func (b *backend) shutdown(ctx context.Context, log *slog.Logger) {
	// Close the feed directly too, in case Serve hasn't registered it with the server yet.
	b.conns.Close()
	if err := b.srv.Shutdown(ctx); err != nil {
		log.Warn("SMTP server shutdown timed out before all sessions drained")
		b.srv.Close()
	}
}

// chanListener is a net.Listener fed by an endpoint's accept loop.
type chanListener struct {
	addr   net.Addr
	ch     chan net.Conn
	closed chan struct{}
	once   sync.Once
}

func newChanListener(addr net.Addr) *chanListener {
	return &chanListener{addr: addr, ch: make(chan net.Conn), closed: make(chan struct{})}
}

func (l *chanListener) Accept() (net.Conn, error) {
	select {
	case c := <-l.ch:
		return c, nil
	case <-l.closed:
		return nil, net.ErrClosed
	}
}

func (l *chanListener) Close() error {
	l.once.Do(func() { close(l.closed) })
	return nil
}

func (l *chanListener) Addr() net.Addr { return l.addr }

func (l *chanListener) deliver(c net.Conn) bool {
	select {
	case l.ch <- c:
		return true
	case <-l.closed:
		return false
	}
}

// connLimiter caps concurrent connections overall and per remote IP, so a public port
// 25 can't be used to exhaust memory with many simultaneous large messages.
type connLimiter struct {
	log          *slog.Logger
	max, perIP   int
	mu           sync.Mutex
	total        int
	byIP         map[string]int
	lastRejectAt time.Time
}

func newConnLimiter(log *slog.Logger, maxConns, perIP int) *connLimiter {
	return &connLimiter{log: log, max: maxConns, perIP: perIP, byIP: map[string]int{}}
}

func (l *connLimiter) acquire(c net.Conn, port int) (net.Conn, bool) {
	ip := c.RemoteAddr().String()
	if host, _, err := net.SplitHostPort(ip); err == nil {
		ip = host
	}
	l.mu.Lock()
	defer l.mu.Unlock()
	if (l.max > 0 && l.total >= l.max) || (l.perIP > 0 && l.byIP[ip] >= l.perIP) {
		// Rate-limit this log line so a flood can't flood the logs too.
		if time.Since(l.lastRejectAt) > time.Second {
			l.log.Warn("Rejected SMTP connection: connection limit reached", "remote", ip, "port", port,
				"active", l.total, "activeFromIp", l.byIP[ip])
			l.lastRejectAt = time.Now()
		}
		return nil, false
	}
	l.total++
	l.byIP[ip]++
	return &limitedConn{Conn: c, release: func() { l.release(ip) }}, true
}

func (l *connLimiter) release(ip string) {
	l.mu.Lock()
	defer l.mu.Unlock()
	l.total--
	if l.byIP[ip]--; l.byIP[ip] <= 0 {
		delete(l.byIP, ip)
	}
}

// limitedConn returns its limiter slot when closed. go-smtp closes every connection
// (QUIT, timeout, error or shutdown), and TLS wrappers close the underlying conn.
type limitedConn struct {
	net.Conn
	once    sync.Once
	release func()
}

func (c *limitedConn) Close() error {
	c.once.Do(c.release)
	return c.Conn.Close()
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
