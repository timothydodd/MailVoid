package smtpd

import (
	"context"
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/json"
	"encoding/pem"
	"math/big"
	"net"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"
)

// A header block of a million short folded lines must parse in linear time (it was
// quadratic, letting one message pin a CPU core for hours).
func TestFlattenHeaders_LinearOnHugeFoldedHeader(t *testing.T) {
	raw := "X-Big: a\r\n" + strings.Repeat(" a\r\n", 1_000_000) + "Subject: hi\r\n\r\nbody"
	start := time.Now()
	h := FlattenHeaders([]byte(raw))
	if d := time.Since(start); d > 2*time.Second {
		t.Fatalf("FlattenHeaders took %v", d)
	}
	// Collection stops at the header block cap; the raw source keeps the rest.
	if v, _ := h.Get("X-Big"); len(v) > maxHeaderBlockBytes {
		t.Fatalf("X-Big value is %d bytes, cap is %d", len(v), maxHeaderBlockBytes)
	}

	start = time.Now()
	data, _ := BuildEmailData([]byte(raw), "a@example.com", []string{"b@mailvoid.com"})
	if d := time.Since(start); d > 5*time.Second {
		t.Fatalf("BuildEmailData took %v", d)
	}
	if data.RawEmail == "" {
		t.Fatal("raw source must always be kept")
	}
}

func TestFlattenHeaders_FieldCap(t *testing.T) {
	raw := strings.Repeat("X-A: 1\r\n", maxHeaderFields+500) + "\r\n"
	h := FlattenHeaders([]byte(raw))
	v, _ := h.Get("X-A")
	if n := strings.Count(v, "\n") + 1; n != maxHeaderFields {
		t.Fatalf("collected %d fields, want cap %d", n, maxHeaderFields)
	}
}

func TestMailboxFilter_AcceptsNullReversePath(t *testing.T) {
	f := NewMailboxFilter(discard, FilterOptions{AllowedDomains: []string{"mailvoid.com"}}, 100)
	if !f.CanAcceptFrom("", 0) {
		t.Fatal("MAIL FROM:<> (bounces/DSNs) must be accepted")
	}
	if f.CanAcceptFrom("", 101) {
		t.Fatal("size limit still applies to the null sender")
	}
	if !f.CanDeliverTo("box@mailvoid.com", "") || f.CanDeliverTo("x@gmail.com", "") {
		t.Fatal("recipient checks must still apply to the null sender")
	}
}

// fakeAPI counts webhook posts per recipient and answers with status(recipient).
type fakeAPI struct {
	mu     sync.Mutex
	posts  map[string]int
	status func(rcpt string, attempt int) int
}

func (a *fakeAPI) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	var body struct{ To string }
	_ = json.NewDecoder(r.Body).Decode(&body)
	a.mu.Lock()
	a.posts[body.To]++
	n := a.posts[body.To]
	a.mu.Unlock()
	w.WriteHeader(a.status(body.To, n))
}

func newForwardTest(t *testing.T, status func(string, int) int) (*fakeAPI, *Forwarder, *Queue) {
	api := &fakeAPI{posts: map[string]int{}, status: status}
	srv := httptest.NewServer(api)
	t.Cleanup(srv.Close)
	fwd := NewForwarder(discard, []APITarget{{Name: "t", BaseUrl: srv.URL, WebhookEndpoint: "/api/webhook/mail", Enabled: true}})
	return api, fwd, newTestQueue(t, t.TempDir(), 3)
}

func twoRecipients() EmailWebhookData {
	d := sample()
	d.To = []string{"a@mailvoid.com", "b@mailvoid.com"}
	return d
}

// A permanent 4xx for one recipient must not redeliver the others; the message ends up
// in dead-letter/ for inspection.
func TestForward_PermanentRejectDoesNotRedeliverOthers(t *testing.T) {
	api, fwd, q := newForwardTest(t, func(rcpt string, _ int) int {
		if rcpt == "b@mailvoid.com" {
			return http.StatusBadRequest
		}
		return http.StatusOK
	})
	q.Enqueue(context.Background(), twoRecipients())
	process(context.Background(), q, fwd, loadNext(t, q))

	if api.posts["a@mailvoid.com"] != 1 || api.posts["b@mailvoid.com"] != 1 {
		t.Fatalf("posts = %v (a 4xx must not be retried)", api.posts)
	}
	if total, _ := q.DeadLetterStats(); total != 1 {
		t.Fatalf("dead-lettered = %d, want 1", total)
	}
}

// A transient failure for one recipient retries only that recipient.
func TestForward_RetryOnlyResendsFailedPairs(t *testing.T) {
	api, fwd, q := newForwardTest(t, func(rcpt string, attempt int) int {
		if rcpt == "b@mailvoid.com" && attempt <= 2 { // both in-call tries of the first pass fail
			return http.StatusServiceUnavailable
		}
		return http.StatusOK
	})
	q.Enqueue(context.Background(), twoRecipients())
	process(context.Background(), q, fwd, loadNext(t, q))

	q.requeueDue(time.Now().Add(time.Hour))
	item := loadNext(t, q)
	if len(item.Delivered) != 1 {
		t.Fatalf("persisted Delivered = %v", item.Delivered)
	}
	process(context.Background(), q, fwd, item)

	if api.posts["a@mailvoid.com"] != 1 {
		t.Fatalf("a was redelivered: posts = %v", api.posts)
	}
	if api.posts["b@mailvoid.com"] != 3 {
		t.Fatalf("b posts = %d, want 3", api.posts["b@mailvoid.com"])
	}
	if total, _ := q.DeadLetterStats(); total != 0 || q.PendingCount() != 0 {
		t.Fatalf("dead=%d pending=%d, want processed", total, q.PendingCount())
	}
}

func TestForward_408And429AreRetryable(t *testing.T) {
	for _, code := range []int{http.StatusRequestTimeout, http.StatusTooManyRequests} {
		if permanentStatus(code) {
			t.Errorf("%d must be retryable", code)
		}
	}
	if !permanentStatus(http.StatusBadRequest) || permanentStatus(http.StatusBadGateway) {
		t.Error("400 is permanent, 502 is not")
	}
}

func writeSelfSigned(t *testing.T, path string) {
	t.Helper()
	key, _ := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	tmpl := &x509.Certificate{SerialNumber: big.NewInt(1), Subject: pkix.Name{CommonName: "test"},
		NotBefore: time.Now().Add(-time.Hour), NotAfter: time.Now().Add(90 * 24 * time.Hour)}
	der, _ := x509.CreateCertificate(rand.Reader, tmpl, tmpl, &key.PublicKey, key)
	keyDER, _ := x509.MarshalECPrivateKey(key)
	out := pem.EncodeToMemory(&pem.Block{Type: "CERTIFICATE", Bytes: der})
	out = append(out, pem.EncodeToMemory(&pem.Block{Type: "EC PRIVATE KEY", Bytes: keyDER})...)
	if err := os.WriteFile(path, out, 0o600); err != nil {
		t.Fatal(err)
	}
}

// A failed reload (e.g. a half-written renewal) keeps the working certificate.
func TestCertStore_KeepsPreviousCertOnFailedReload(t *testing.T) {
	path := filepath.Join(t.TempDir(), "cert.pem")
	writeSelfSigned(t, path)
	s := NewCertStore(discard, ServerOptions{EnableSsl: true, CertificatePath: path}, LetsEncryptOptions{})
	if !s.Reload() {
		t.Fatal("initial load failed")
	}
	first := s.Current()

	os.WriteFile(path, []byte("-----BEGIN CERTIFICATE-----\ngarbage"), 0o600)
	if !s.Reload() || s.Current() != first {
		t.Fatal("previous certificate should stay active after a failed reload")
	}
}

type fakeConn struct {
	net.Conn
	addr   string
	closed bool
}

func (c *fakeConn) RemoteAddr() net.Addr {
	a, _ := net.ResolveTCPAddr("tcp", c.addr)
	return a
}
func (c *fakeConn) Close() error { c.closed = true; return nil }

func TestConnLimiter(t *testing.T) {
	l := newConnLimiter(discard, 3, 2)
	var held []net.Conn
	for i := 0; i < 2; i++ {
		c, ok := l.acquire(&fakeConn{addr: "10.0.0.1:1000"}, 25)
		if !ok {
			t.Fatal("under the per-IP limit")
		}
		held = append(held, c)
	}
	if _, ok := l.acquire(&fakeConn{addr: "10.0.0.1:1001"}, 25); ok {
		t.Fatal("per-IP limit not enforced")
	}
	c3, ok := l.acquire(&fakeConn{addr: "10.0.0.2:1000"}, 25)
	if !ok {
		t.Fatal("another IP should still connect")
	}
	if _, ok := l.acquire(&fakeConn{addr: "10.0.0.3:1000"}, 25); ok {
		t.Fatal("total limit not enforced")
	}
	// Closing (twice, as TLS wrappers may) releases exactly one slot.
	held[0].Close()
	held[0].Close()
	if _, ok := l.acquire(&fakeConn{addr: "10.0.0.1:1002"}, 25); !ok {
		t.Fatal("slot not released on close")
	}
	c3.Close()
}
