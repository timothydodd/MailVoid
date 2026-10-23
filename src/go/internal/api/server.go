package api

import (
	"bufio"
	"bytes"
	"database/sql"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log/slog"
	"net"
	"net/http"
	"net/netip"
	"path/filepath"
	"regexp"
	"strings"
	"sync"
	"time"

	"mailvoid/internal/config"
)

type Server struct {
	db          *sql.DB
	log         *slog.Logger
	jwt         JWTSettings
	apiKeys     []APIKey
	corsOrigins []string
	webRoot     string
	isDev       bool
	hub         *Hub
	static      *staticCache

	trustedProxies []netip.Prefix

	loginLimiter   *rateLimiter
	webhookLimiter *rateLimiter
	// webhookGlobal caps anonymous captures across all sources, so many addresses
	// together can't fill the disk faster than retention reclaims it.
	webhookGlobal *rateLimiter

	maxWebhookBuckets    int // auto-created bucket cap
	maxWebhooksPerBucket int // oldest captures beyond this are pruned
}

func NewServer(cfg *config.Config, db *sql.DB, log *slog.Logger) (*Server, error) {
	secret := cfg.Get("JwtSettings:Secret")
	if strings.TrimSpace(secret) == "" || len(secret) < 32 {
		return nil, errors.New("JwtSettings:Secret must be configured and at least 32 characters long")
	}
	expiry := cfg.Int("JwtSettings:ExpiryMinutes", 0)
	if expiry <= 0 {
		return nil, errors.New("JWT ExpiryMinutes must be a positive integer")
	}
	s := &Server{
		db:  db,
		log: log,
		jwt: JWTSettings{
			Secret:                 secret,
			Issuer:                 cfg.Get("JwtSettings:Issuer"),
			Audience:               cfg.Get("JwtSettings:Audience"),
			ExpiryMinutes:          expiry,
			RefreshTokenExpiryDays: cfg.Int("JwtSettings:RefreshTokenExpiryDays", 30),
			CookieSameSite:         parseSameSite(cfg.String("JwtSettings:RefreshTokenCookieSameSite", "None")),
			CookieSecure:           cfg.Bool("JwtSettings:RefreshTokenCookieSecure", true),
		},
		corsOrigins:    cfg.Strings("CorsOrigins"),
		webRoot:        cfg.String("WebRoot", filepath.Join(cfg.ContentRoot, "wwwroot")),
		isDev:          cfg.IsDevelopment(),
		hub:            newHub(log),
		loginLimiter:   newRateLimiter(5, time.Minute),
		webhookLimiter: newRateLimiter(60, time.Minute),
		webhookGlobal:  newRateLimiter(max(1, cfg.Int("Webhooks:MaxCapturesPerMinute", 300)), time.Minute),

		maxWebhookBuckets:    cfg.Int("Webhooks:MaxBuckets", 100),
		maxWebhooksPerBucket: cfg.Int("Webhooks:MaxWebhooksPerBucket", 1000),
		static:               newStaticCache(),
		trustedProxies:       loadTrustedProxies(cfg),
	}
	if len(cfg.Strings("ForwardedHeaders:KnownNetworks")) == 0 && len(cfg.Strings("ForwardedHeaders:KnownProxies")) == 0 {
		log.Info("trusting X-Forwarded-For from loopback and private networks; set ForwardedHeaders:KnownProxies if clients can reach the API directly through NAT")
	}
	if s.jwt.Issuer == "" || s.jwt.Audience == "" {
		return nil, errors.New("JwtSettings:Issuer and JwtSettings:Audience must be configured")
	}
	for _, i := range cfg.Children("ApiKeys") {
		p := "ApiKeys:" + i + ":"
		s.apiKeys = append(s.apiKeys, APIKey{
			Name:    cfg.Get(p + "Name"),
			Key:     cfg.Get(p + "Key"),
			Enabled: cfg.Bool(p+"Enabled", true),
			Scopes:  cfg.Strings(p + "Scopes"),
		})
	}
	return s, nil
}

func parseSameSite(v string) http.SameSite {
	switch strings.ToLower(v) {
	case "lax":
		return http.SameSiteLaxMode
	case "strict":
		return http.SameSiteStrictMode
	case "unspecified":
		return http.SameSiteDefaultMode
	}
	return http.SameSiteNoneMode
}

func (s *Server) Handler() http.Handler {
	mux := http.NewServeMux()
	auth := s.requireAuth

	// Auth
	mux.HandleFunc("POST /api/auth/login", s.rateLimit(s.loginLimiter, s.handleLogin))
	mux.HandleFunc("GET /api/auth/user", auth(s.handleGetUser))
	mux.HandleFunc("POST /api/auth/change-password", auth(s.handleChangePassword))
	mux.HandleFunc("POST /api/auth/refresh", s.handleRefresh)
	mux.HandleFunc("POST /api/auth/revoke", auth(s.handleRevoke))
	mux.HandleFunc("POST /api/auth/logout", auth(s.handleLogout))

	// Mail
	mux.HandleFunc("GET /api/mail/{id}", auth(s.handleGetMail))
	mux.HandleFunc("GET /api/mail/boxes", auth(s.handleGetBoxes))
	mux.HandleFunc("POST /api/mail", auth(s.handleGetMails))
	mux.HandleFunc("DELETE /api/mail/boxes", auth(s.handleDeleteBox))
	mux.HandleFunc("GET /api/mail/groups", auth(s.handleGetMailGroups))
	mux.HandleFunc("POST /api/mail/groups/{id}/hide", auth(s.handleHideMailGroup))
	mux.HandleFunc("DELETE /api/mail/groups/{id}/hide", auth(s.handleUnhideMailGroup))
	mux.HandleFunc("GET /api/mail/groups/all", auth(s.handleGetAllMailGroups))
	mux.HandleFunc("POST /api/mail/groups/{id}/access", auth(s.handleGrantAccess))
	mux.HandleFunc("DELETE /api/mail/groups/{id}/access/{userId}", auth(s.handleRevokeAccess))
	mux.HandleFunc("PUT /api/mail/groups/{id}", auth(s.handleUpdateMailGroup))
	mux.HandleFunc("GET /api/mail/groups/{id}/users", auth(s.handleGetMailGroupUsers))
	mux.HandleFunc("GET /api/mail/users", auth(s.handleGetUsers))
	mux.HandleFunc("POST /api/mail/mail-groups", auth(s.handleCreateMailGroup))
	mux.HandleFunc("DELETE /api/mail/mail-groups/{id}", auth(s.handleDeleteMailGroup))
	mux.HandleFunc("GET /api/mail/mailbox/{id}/retention", auth(s.handleGetRetention))
	mux.HandleFunc("PUT /api/mail/mailbox/{id}/retention", auth(s.handleUpdateRetention))
	mux.HandleFunc("POST /api/mail/mark-all-read", auth(s.handleMarkAllAsRead))

	// Mail server webhook (SMTP server -> API)
	mux.HandleFunc("POST /api/webhook/mail", s.requireAPIKey(s.handleMailWebhook))

	// Anonymous webhook capture
	for _, m := range []string{"POST", "PUT", "PATCH"} {
		capture := s.rateLimit(s.webhookLimiter, s.globalLimit(s.webhookGlobal, s.handleCaptureWebhook))
		mux.HandleFunc(m+" /api/hook/{bucket}", capture)
		mux.HandleFunc(m+" /api/hook/{bucket}/{path...}", capture)
	}

	// Webhook management
	mux.HandleFunc("GET /api/webhooks/buckets", auth(s.handleGetBuckets))
	mux.HandleFunc("GET /api/webhooks/buckets/{name}", auth(s.handleGetBucket))
	mux.HandleFunc("GET /api/webhooks/buckets/{name}/webhooks", auth(s.handleGetWebhooks))
	mux.HandleFunc("GET /api/webhooks/{id}", auth(s.handleGetWebhook))
	mux.HandleFunc("DELETE /api/webhooks/{id}", auth(s.handleDeleteWebhook))
	mux.HandleFunc("DELETE /api/webhooks/buckets/{name}", auth(s.handleDeleteBucket))
	mux.HandleFunc("DELETE /api/webhooks/buckets/{name}/webhooks", auth(s.handlePurgeBucket))
	mux.HandleFunc("DELETE /api/webhooks", auth(s.handlePurgeAll))

	// User management. ASP.NET routing is case-insensitive and the route came from
	// [controller], so accept both spellings.
	for _, base := range []string{"/api/UserManagement", "/api/usermanagement"} {
		mux.HandleFunc("GET "+base, auth(s.handleListUsers))
		mux.HandleFunc("GET "+base+"/{id}", auth(s.handleGetUserByID))
		mux.HandleFunc("POST "+base, auth(s.handleCreateUser))
		mux.HandleFunc("PUT "+base+"/{id}/role", auth(s.handleUpdateUserRole))
		mux.HandleFunc("PUT "+base+"/{id}/password", auth(s.handleUpdateUserPassword))
		mux.HandleFunc("DELETE "+base+"/{id}", auth(s.handleDeleteUser))
	}

	mux.HandleFunc("/api/health", s.handleHealth)

	// SignalR hub
	mux.HandleFunc("POST /hubs/mail/negotiate", auth(s.hub.handleNegotiate))
	mux.HandleFunc("/hubs/mail", auth(s.hub.handleConnect))

	mux.HandleFunc("/", s.handleStatic)

	var h http.Handler = mux
	h = s.securityHeaders(h)
	h = s.cors(h)
	h = compressResponses(h)
	h = decompressRequests(h)
	h = readDeadlines(h)
	h = s.recoverPanics(h)
	h = s.requestLogging(h)
	return h
}

// ---- Response helpers ----

func writeJSON(w http.ResponseWriter, status int, v any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(v)
}

func writeText(w http.ResponseWriter, status int, msg string) {
	w.Header().Set("Content-Type", "text/plain; charset=utf-8")
	w.WriteHeader(status)
	_, _ = io.WriteString(w, msg)
}

// writeProblem matches the ProblemDetails body [ApiController] returns for bare
// NotFound()/BadRequest()/Unauthorized() results.
func writeProblem(w http.ResponseWriter, status int) {
	types := map[int]string{
		400: "https://tools.ietf.org/html/rfc9110#section-15.5.1",
		401: "https://tools.ietf.org/html/rfc9110#section-15.5.2",
		404: "https://tools.ietf.org/html/rfc9110#section-15.5.5",
	}
	w.Header().Set("Content-Type", "application/problem+json; charset=utf-8")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(map[string]any{
		"type":   types[status],
		"title":  http.StatusText(status),
		"status": status,
	})
}

func writeMessage(w http.ResponseWriter, status int, msg string) {
	writeJSON(w, status, map[string]string{"message": msg})
}

// forbid is ControllerBase.Forbid(): an empty 403.
func forbid(w http.ResponseWriter) { w.WriteHeader(http.StatusForbidden) }

// decodeBody reads a JSON request body. An empty body is allowed when allowEmpty is set
// (EmptyBodyBehavior.Allow); otherwise it is a 400 like ASP.NET model binding.
func decodeBody(r *http.Request, v any, allowEmpty bool) error {
	data, err := io.ReadAll(r.Body)
	if err != nil {
		return err
	}
	if len(bytes.TrimSpace(data)) == 0 {
		if allowEmpty {
			return nil
		}
		return errors.New("request body is required")
	}
	return json.Unmarshal(data, v)
}

func (s *Server) internalError(w http.ResponseWriter, r *http.Request, err error) {
	s.log.Error("request failed", "method", r.Method, "path", r.URL.Path, "error", err)
	writeJSON(w, http.StatusInternalServerError, map[string]any{
		"error": map[string]any{
			"message":   "An internal server error occurred",
			"type":      "Exception",
			"timestamp": Now(),
			"path":      r.URL.Path,
			"method":    r.Method,
		},
	})
}

// ---- Middleware ----

func (s *Server) securityHeaders(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		h := w.Header()
		h.Set("X-Content-Type-Options", "nosniff")
		h.Set("X-Frame-Options", "DENY")
		h.Set("Referrer-Policy", "no-referrer")
		h.Set("Permissions-Policy", "camera=(), microphone=(), geolocation=()")
		if r.URL.Path == "/api" || strings.HasPrefix(r.URL.Path, "/api/") {
			h.Set("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'")
		} else {
			// The SPA's own policy is a <meta> tag in index.html; frame-ancestors only works
			// as a header, and multiple policies intersect, so this adds without loosening.
			h.Set("Content-Security-Policy", "frame-ancestors 'none'")
		}
		if s.isHTTPS(r) {
			h.Set("Strict-Transport-Security", "max-age=31536000")
		}
		next.ServeHTTP(w, r)
	})
}

// isHTTPS reports whether the client connected over TLS: directly, or via a trusted
// proxy that says so in X-Forwarded-Proto.
func (s *Server) isHTTPS(r *http.Request) bool {
	if r.TLS != nil {
		return true
	}
	peer, err := netip.ParseAddr(remoteIP(r))
	if err != nil || !s.isTrustedProxy(peer) {
		return false
	}
	proto, _, _ := strings.Cut(r.Header.Get("X-Forwarded-Proto"), ",")
	return strings.EqualFold(strings.TrimSpace(proto), "https")
}

// cors reproduces the "MailVoidOrigins" policy: configured origins with credentials, or
// any origin without credentials when none are configured. X-Total-Count is exposed.
func (s *Server) cors(next http.Handler) http.Handler {
	allowed := map[string]bool{}
	for _, o := range s.corsOrigins {
		allowed[strings.ToLower(strings.TrimRight(o, "/"))] = true
	}
	anyOrigin := len(allowed) == 0

	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		origin := r.Header.Get("Origin")
		preflight := r.Method == http.MethodOptions && r.Header.Get("Access-Control-Request-Method") != ""
		if origin == "" {
			next.ServeHTTP(w, r)
			return
		}
		h := w.Header()
		ok := anyOrigin || allowed[strings.ToLower(origin)]
		if ok {
			if anyOrigin {
				h.Set("Access-Control-Allow-Origin", "*")
			} else {
				h.Set("Access-Control-Allow-Origin", origin)
				h.Set("Access-Control-Allow-Credentials", "true")
				h.Add("Vary", "Origin")
			}
		}
		if preflight {
			if ok {
				h.Set("Access-Control-Allow-Methods", r.Header.Get("Access-Control-Request-Method"))
				if rh := r.Header.Get("Access-Control-Request-Headers"); rh != "" {
					h.Set("Access-Control-Allow-Headers", rh)
				}
			}
			w.WriteHeader(http.StatusNoContent)
			return
		}
		if ok {
			h.Set("Access-Control-Expose-Headers", "X-Total-Count")
		}
		next.ServeHTTP(w, r)
	})
}

func (s *Server) recoverPanics(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		defer func() {
			if v := recover(); v != nil {
				if v == http.ErrAbortHandler {
					panic(v)
				}
				s.internalError(w, r, fmt.Errorf("panic: %v", v))
			}
		}()
		next.ServeHTTP(w, r)
	})
}

var sensitiveQuery = regexp.MustCompile(`(?i)([?&](?:api_key|access_token)=)[^&]*`)

func redactQuery(rawQuery string) string {
	if rawQuery == "" {
		return ""
	}
	return sensitiveQuery.ReplaceAllString("?"+rawQuery, "${1}***")
}

func (s *Server) requestLogging(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		p := strings.ToLower(r.URL.Path)
		if !strings.HasPrefix(p, "/api") && !strings.HasPrefix(p, "/hubs") {
			next.ServeHTTP(w, r)
			return
		}
		start := time.Now()
		sw := &statusWriter{ResponseWriter: w, status: http.StatusOK}
		next.ServeHTTP(sw, r)
		level := slog.LevelInfo
		if sw.status >= 400 {
			level = slog.LevelWarn
		}
		s.log.Log(r.Context(), level, "HTTP request",
			"method", r.Method, "path", r.URL.Path+redactQuery(r.URL.RawQuery),
			"status", sw.status, "durationMs", time.Since(start).Milliseconds(), "ip", s.clientIP(r))
	})
}

type statusWriter struct {
	http.ResponseWriter
	status int
}

func (w *statusWriter) WriteHeader(code int) {
	w.status = code
	w.ResponseWriter.WriteHeader(code)
}

func (w *statusWriter) Unwrap() http.ResponseWriter { return w.ResponseWriter }

func (w *statusWriter) Hijack() (net.Conn, *bufio.ReadWriter, error) {
	return http.NewResponseController(w.ResponseWriter).Hijack()
}

func (w *statusWriter) Flush() { _ = http.NewResponseController(w.ResponseWriter).Flush() }

// ---- Rate limiting (fixed window per remote IP) ----

type rateLimiter struct {
	mu      sync.Mutex
	limit   int
	window  time.Duration
	buckets map[string]*rateWindow
}

type rateWindow struct {
	start time.Time
	count int
}

func newRateLimiter(limit int, window time.Duration) *rateLimiter {
	return &rateLimiter{limit: limit, window: window, buckets: map[string]*rateWindow{}}
}

func (l *rateLimiter) allow(key string) bool {
	l.mu.Lock()
	defer l.mu.Unlock()
	now := time.Now()
	if len(l.buckets) > 10000 {
		for k, b := range l.buckets {
			if now.Sub(b.start) >= l.window {
				delete(l.buckets, k)
			}
		}
	}
	b := l.buckets[key]
	if b == nil || now.Sub(b.start) >= l.window {
		l.buckets[key] = &rateWindow{start: now, count: 1}
		return true
	}
	if b.count >= l.limit {
		return false
	}
	b.count++
	return true
}

// globalLimit applies one shared window to every caller.
func (s *Server) globalLimit(l *rateLimiter, next http.HandlerFunc) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		if !l.allow("*") {
			w.WriteHeader(http.StatusTooManyRequests)
			return
		}
		next(w, r)
	}
}

func (s *Server) rateLimit(l *rateLimiter, next http.HandlerFunc) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		if !l.allow(s.clientIP(r)) {
			w.WriteHeader(http.StatusTooManyRequests)
			return
		}
		next(w, r)
	}
}
