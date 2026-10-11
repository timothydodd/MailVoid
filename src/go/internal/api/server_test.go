package api

import (
	"bytes"
	"compress/gzip"
	"context"
	"encoding/json"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"net/netip"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"

	"github.com/andybalholm/brotli"

	"mailvoid/internal/config"
)

func TestClientIP(t *testing.T) {
	s := &Server{trustedProxies: loadTrustedProxies(&config.Config{})}
	req := func(remote string, xff ...string) *http.Request {
		r := httptest.NewRequest("GET", "/", nil)
		r.RemoteAddr = remote
		for _, v := range xff {
			r.Header.Add("X-Forwarded-For", v)
		}
		return r
	}
	cases := []struct {
		name string
		r    *http.Request
		want string
	}{
		{"direct client, header ignored", req("203.0.113.9:5000", "1.2.3.4"), "203.0.113.9"},
		{"via ingress", req("10.42.0.5:5000", "198.51.100.7"), "198.51.100.7"},
		{"spoofed left-most entry ignored", req("10.42.0.5:5000", "6.6.6.6, 198.51.100.7"), "198.51.100.7"},
		{"chained trusted proxies", req("10.42.0.5:5000", "198.51.100.7, 10.42.0.9"), "198.51.100.7"},
		{"no header", req("10.42.0.5:5000"), "10.42.0.5"},
		{"malformed header", req("10.42.0.5:5000", "garbage"), "10.42.0.5"},
	}
	for _, c := range cases {
		if got := s.clientIP(c.r); got != c.want {
			t.Errorf("%s: got %s, want %s", c.name, got, c.want)
		}
	}

	custom := &Server{trustedProxies: []netip.Prefix{netip.MustParsePrefix("192.0.2.0/24")}}
	if got := custom.clientIP(req("10.42.0.5:5000", "198.51.100.7")); got != "10.42.0.5" {
		t.Errorf("only configured networks should be trusted, got %s", got)
	}
}

func TestAcceptedEncoding(t *testing.T) {
	cases := map[string]string{
		"gzip, deflate, br": "br",
		"gzip":              "gzip",
		"br;q=0, gzip":      "gzip",
		"identity":          "",
		"":                  "",
	}
	for in, want := range cases {
		if got := acceptedEncoding(in); got != want {
			t.Errorf("acceptedEncoding(%q) = %q, want %q", in, got, want)
		}
	}
}

// ---- Integration tests over the full handler stack ----

type testEnv struct {
	t       *testing.T
	srv     *Server
	handler http.Handler
	web     string
}

func newTestEnv(t *testing.T) *testEnv {
	t.Helper()
	dir := t.TempDir()
	web := filepath.Join(dir, "wwwroot")
	_ = os.MkdirAll(web, 0o755)
	cfg, err := config.Load(config.Options{Args: []string{
		"--contentRoot", dir,
		"--JwtSettings:Secret=integration-test-secret-at-least-32-chars",
		"--JwtSettings:Issuer=mailvoid", "--JwtSettings:Audience=mailvoid", "--JwtSettings:ExpiryMinutes=30",
		"--WebRoot=" + web,
	}})
	if err != nil {
		t.Fatal(err)
	}
	db, err := OpenDatabase("Data Source=" + filepath.Join(dir, "test.db"))
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { db.Close() })
	if err := InitSchema(context.Background(), db); err != nil {
		t.Fatal(err)
	}
	srv, err := NewServer(cfg, db, slog.New(slog.NewTextHandler(io.Discard, nil)))
	if err != nil {
		t.Fatal(err)
	}
	if err := srv.SeedDefaultData(context.Background()); err != nil {
		t.Fatal(err)
	}
	return &testEnv{t: t, srv: srv, handler: srv.Handler(), web: web}
}

func (e *testEnv) do(method, path, token string, body any, hdr ...string) *httptest.ResponseRecorder {
	var rdr io.Reader
	switch b := body.(type) {
	case nil:
	case []byte:
		rdr = bytes.NewReader(b)
	default:
		data, _ := json.Marshal(b)
		rdr = bytes.NewReader(data)
	}
	r := httptest.NewRequest(method, path, rdr)
	r.Header.Set("Content-Type", "application/json")
	if token != "" {
		r.Header.Set("Authorization", "Bearer "+token)
	}
	for i := 0; i+1 < len(hdr); i += 2 {
		r.Header.Set(hdr[i], hdr[i+1])
	}
	w := httptest.NewRecorder()
	e.handler.ServeHTTP(w, r)
	return w
}

func (e *testEnv) login(user, pass string) (token, refresh string) {
	w := e.do("POST", "/api/auth/login", "", map[string]string{"userName": user, "password": pass})
	if w.Code != 200 {
		e.t.Fatalf("login %s: %d %s", user, w.Code, w.Body)
	}
	var resp loginResponse
	_ = json.Unmarshal(w.Body.Bytes(), &resp)
	for _, c := range w.Result().Cookies() {
		if c.Name == refreshCookieName {
			refresh = c.Value
		}
	}
	return resp.AccessToken, refresh
}

func (e *testEnv) createUser(adminToken, name string) GUID {
	w := e.do("POST", "/api/UserManagement", adminToken, map[string]any{"userName": name, "password": "pw", "role": 0})
	if w.Code != 201 {
		e.t.Fatalf("create user: %d %s", w.Code, w.Body)
	}
	var dto userDto
	_ = json.Unmarshal(w.Body.Bytes(), &dto)
	id, _ := ParseGUID(dto.ID)
	return id
}

func TestPublicGroupCannotBeManagedByOthers(t *testing.T) {
	e := newTestEnv(t)
	admin, _ := e.login("admin", "admin")
	alice := e.createUser(admin, "alice")
	e.createUser(admin, "mallory")
	aliceTok, _ := e.login("alice", "pw")
	malTok, _ := e.login("mallory", "pw")

	w := e.do("POST", "/api/mail/mail-groups", aliceTok, map[string]any{"subdomain": "Team-A", "isPublic": true})
	if w.Code != 200 {
		t.Fatalf("create group: %d %s", w.Code, w.Body)
	}
	var g struct {
		ID        int64  `json:"id"`
		Subdomain string `json:"subdomain"`
	}
	_ = json.Unmarshal(w.Body.Bytes(), &g)
	if g.Subdomain != "team-a" {
		t.Errorf("subdomain should be normalised, got %q", g.Subdomain)
	}
	base := "/api/mail/groups/" + itoa64(g.ID)

	for _, c := range []struct {
		name, method, path string
		body               any
	}{
		{"self-grant", "POST", base + "/access", map[string]string{"userId": "00000000-0000-0000-0000-000000000000"}},
		{"revoke", "DELETE", base + "/access/" + alice.String(), nil},
		{"update", "PUT", base, map[string]any{"description": "pwned", "isPublic": false}},
		{"retention", "PUT", "/api/mail/mailbox/" + itoa64(g.ID) + "/retention", map[string]any{"retentionDays": 1}},
		{"member list", "GET", base + "/users", nil},
	} {
		if w := e.do(c.method, c.path, malTok, c.body); w.Code != http.StatusForbidden {
			t.Errorf("%s by non-owner: got %d, want 403", c.name, w.Code)
		}
	}

	// The owner and admins still can.
	if w := e.do("PUT", "/api/mail/mailbox/"+itoa64(g.ID)+"/retention", aliceTok, map[string]any{"retentionDays": 7}); w.Code != 200 {
		t.Errorf("owner retention update: %d", w.Code)
	}
	if w := e.do("PUT", base, admin, map[string]any{"description": "ok", "isPublic": true}); w.Code != 200 {
		t.Errorf("admin update: %d", w.Code)
	}
	if w := e.do("POST", "/api/mail/mail-groups", aliceTok, map[string]any{"subdomain": "bad.name"}); w.Code != 400 {
		t.Errorf("invalid subdomain should be rejected, got %d", w.Code)
	}
}

func TestRefreshTokenCannotBeRedeemedTwice(t *testing.T) {
	e := newTestEnv(t)
	access, refresh := e.login("admin", "admin")
	body := map[string]string{"accessToken": access, "refreshToken": refresh}

	var wg sync.WaitGroup
	codes := make([]int, 5)
	for i := range codes {
		wg.Add(1)
		go func(i int) {
			defer wg.Done()
			codes[i] = e.do("POST", "/api/auth/refresh", "", body).Code
		}(i)
	}
	wg.Wait()
	ok := 0
	for _, c := range codes {
		if c == 200 {
			ok++
		}
	}
	if ok != 1 {
		t.Errorf("exactly one concurrent refresh should succeed, got codes %v", codes)
	}
}

func TestPasswordChangeRevokesOtherSessions(t *testing.T) {
	e := newTestEnv(t)
	accessA, refreshA := e.login("admin", "admin")
	accessB, refreshB := e.login("admin", "admin")

	r := httptest.NewRequest("POST", "/api/auth/change-password", strings.NewReader(`{"oldPassword":"admin","newPassword":"new-pass"}`))
	r.Header.Set("Authorization", "Bearer "+accessA)
	r.AddCookie(&http.Cookie{Name: refreshCookieName, Value: refreshA})
	w := httptest.NewRecorder()
	e.handler.ServeHTTP(w, r)
	if w.Code != 200 {
		t.Fatalf("change password: %d", w.Code)
	}

	if w := e.do("POST", "/api/auth/refresh", "", map[string]string{"accessToken": accessB, "refreshToken": refreshB}); w.Code != 401 {
		t.Errorf("other session's refresh token should be revoked, got %d", w.Code)
	}
	if w := e.do("POST", "/api/auth/refresh", "", map[string]string{"accessToken": accessA, "refreshToken": refreshA}); w.Code != 200 {
		t.Errorf("the session that changed the password should keep working, got %d", w.Code)
	}
}

func TestBodyLimits(t *testing.T) {
	e := newTestEnv(t)
	big := []byte(`{"userName":"` + strings.Repeat("a", authBodyLimit) + `"}`)
	if w := e.do("POST", "/api/auth/login", "", big); w.Code != http.StatusRequestEntityTooLarge {
		t.Errorf("oversized login body: got %d, want 413", w.Code)
	}

	// A small gzip body that inflates past the limit is cut off after decompression.
	var bomb bytes.Buffer
	zw := gzip.NewWriter(&bomb)
	_, _ = zw.Write([]byte(`{"accessToken":"`))
	_, _ = zw.Write(bytes.Repeat([]byte("a"), 8<<20))
	_ = zw.Close()
	if bomb.Len() > 64<<10 {
		t.Fatalf("test bomb too large: %d", bomb.Len())
	}
	if w := e.do("POST", "/api/auth/refresh", "", bomb.Bytes(), "Content-Encoding", "gzip"); w.Code != http.StatusRequestEntityTooLarge {
		t.Errorf("gzip bomb: got %d, want 413", w.Code)
	}
}

func TestWebhookListTrimsBodies(t *testing.T) {
	e := newTestEnv(t)
	admin, _ := e.login("admin", "admin")
	body := strings.Repeat("x", webhookListBodyChars*3)
	if w := e.do("POST", "/api/hook/b1", "", []byte(body)); w.Code != 200 {
		t.Fatalf("capture: %d %s", w.Code, w.Body)
	}
	var page pagedResults[Webhook]
	_ = json.Unmarshal(e.do("GET", "/api/webhooks/buckets/b1/webhooks", admin, nil).Body.Bytes(), &page)
	if len(page.Items) != 1 || len(page.Items[0].Body) != webhookListBodyChars {
		t.Fatalf("list body should be trimmed to %d, got %+v", webhookListBodyChars, len(page.Items))
	}
	var full Webhook
	_ = json.Unmarshal(e.do("GET", "/api/webhooks/"+itoa64(page.Items[0].ID), admin, nil).Body.Bytes(), &full)
	if len(full.Body) != len(body) {
		t.Errorf("detail should return the full body, got %d", len(full.Body))
	}
}

func TestStaticFilesPrecompressedAndCached(t *testing.T) {
	e := newTestEnv(t)
	js := strings.Repeat("console.log('mailvoid');\n", 2000)
	_ = os.WriteFile(filepath.Join(e.web, "main-ABCD1234.js"), []byte(js), 0o644)
	_ = os.WriteFile(filepath.Join(e.web, "index.html"), []byte("<html>app</html>"), 0o644)

	w := e.do("GET", "/main-ABCD1234.js", "", nil, "Accept-Encoding", "gzip, br")
	if w.Header().Get("Content-Encoding") != "br" || !strings.Contains(w.Header().Get("Cache-Control"), "immutable") {
		t.Fatalf("headers: %v", w.Header())
	}
	decoded, _ := io.ReadAll(brotli.NewReader(w.Body))
	if string(decoded) != js {
		t.Error("brotli body mismatch")
	}
	etag := w.Header().Get("ETag")
	if w := e.do("GET", "/main-ABCD1234.js", "", nil, "Accept-Encoding", "gzip, br", "If-None-Match", etag); w.Code != http.StatusNotModified {
		t.Errorf("conditional request: got %d, want 304", w.Code)
	}
	if w := e.do("GET", "/mail/some/route", "", nil); w.Code != 200 || w.Body.String() != "<html>app</html>" || w.Header().Get("Cache-Control") != "no-cache" {
		t.Errorf("SPA fallback: %d %q %v", w.Code, w.Body.String(), w.Header())
	}
	if w := e.do("GET", "/missing.js", "", nil); w.Code != 404 {
		t.Errorf("missing asset: %d", w.Code)
	}
}

func TestAPIResponsesCompressed(t *testing.T) {
	e := newTestEnv(t)
	admin, _ := e.login("admin", "admin")
	w := e.do("GET", "/api/mail/groups", admin, nil, "Accept-Encoding", "br")
	if w.Header().Get("Content-Encoding") != "br" {
		t.Fatalf("expected brotli, got %v", w.Header())
	}
	decoded, _ := io.ReadAll(brotli.NewReader(w.Body))
	if !json.Valid(decoded) {
		t.Error("brotli JSON did not decode")
	}
}

func itoa64(n int64) string { b, _ := json.Marshal(n); return string(b) }
