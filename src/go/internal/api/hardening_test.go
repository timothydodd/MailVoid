package api

import (
	"context"
	"encoding/json"
	"net/http"
	"strings"
	"testing"
	"time"
)

func TestTokenFollowsCurrentUserState(t *testing.T) {
	e := newTestEnv(t)
	admin, _ := e.login("admin", "admin")
	alice := e.createUser(admin, "alice")
	aliceTok, _ := e.login("alice", "password1")
	users := "/api/UserManagement"

	if w := e.do("GET", users, aliceTok, nil); w.Code != http.StatusForbidden {
		t.Fatalf("plain user listing users: got %d, want 403", w.Code)
	}
	// Promotion takes effect on the existing token...
	if w := e.do("PUT", users+"/"+alice.String()+"/role", admin, map[string]any{"role": "Admin"}); w.Code != http.StatusNoContent {
		t.Fatalf("promote: %d", w.Code)
	}
	if w := e.do("GET", users, aliceTok, nil); w.Code != http.StatusOK {
		t.Errorf("after promotion: got %d, want 200", w.Code)
	}
	// ...and so does demotion, without waiting for the token to expire.
	if w := e.do("PUT", users+"/"+alice.String()+"/role", admin, map[string]any{"role": "User"}); w.Code != http.StatusNoContent {
		t.Fatalf("demote: %d", w.Code)
	}
	if w := e.do("GET", users, aliceTok, nil); w.Code != http.StatusForbidden {
		t.Errorf("after demotion: got %d, want 403", w.Code)
	}
	// A deleted user's token stops working immediately.
	if w := e.do("DELETE", users+"/"+alice.String(), admin, nil); w.Code != http.StatusNoContent {
		t.Fatalf("delete: %d", w.Code)
	}
	if w := e.do("GET", "/api/auth/user", aliceTok, nil); w.Code != http.StatusUnauthorized {
		t.Errorf("deleted user's token: got %d, want 401", w.Code)
	}
}

func TestShortPasswordsRejected(t *testing.T) {
	e := newTestEnv(t)
	admin, _ := e.login("admin", "admin")
	if w := e.do("POST", "/api/UserManagement", admin, map[string]any{"userName": "bob", "password": "short", "role": 0}); w.Code != 400 {
		t.Errorf("create with short password: got %d, want 400", w.Code)
	}
	if w := e.do("POST", "/api/auth/change-password", admin, map[string]string{"oldPassword": "admin", "newPassword": "short"}); w.Code != 400 {
		t.Errorf("change to short password: got %d, want 400", w.Code)
	}
	id := e.createUser(admin, "carol")
	if w := e.do("PUT", "/api/UserManagement/"+id.String()+"/password", admin, map[string]string{"newPassword": "short"}); w.Code != 400 {
		t.Errorf("reset to short password: got %d, want 400", w.Code)
	}
	// The seeded admin/admin still logs in; the rule only applies to new passwords.
	e.login("admin", "admin")
}

func (e *testEnv) insertMail(to, from, subject, text string, created time.Time) {
	e.t.Helper()
	_, err := e.srv.db.Exec(`INSERT INTO Mail ([To], Text, IsHtml, [From], Subject, CreatedOn) VALUES (?, ?, 0, ?, ?, ?)`,
		to, text, from, subject, DBTime{created})
	if err != nil {
		e.t.Fatal(err)
	}
}

func TestMailSearchIsLiteralAndSortable(t *testing.T) {
	e := newTestEnv(t)
	admin, _ := e.login("admin", "admin")
	now := time.Now().UTC()
	e.insertMail("x@a.test", "b@test", "save 50% today", "", now.Add(-3*time.Minute))
	e.insertMail("x@a.test", "a@test", "save 500 today", "", now.Add(-2*time.Minute))
	e.insertMail("x@a.test", "c@test", "under_score", "", now.Add(-1*time.Minute))

	list := func(body map[string]any) []string {
		var page pagedResults[mailWithReadStatus]
		w := e.do("POST", "/api/mail", admin, body)
		if w.Code != 200 {
			t.Fatalf("list: %d %s", w.Code, w.Body)
		}
		_ = json.Unmarshal(w.Body.Bytes(), &page)
		var out []string
		for _, m := range page.Items {
			out = append(out, m.Subject)
		}
		return out
	}
	if got := list(map[string]any{"search": "50%"}); len(got) != 1 || got[0] != "save 50% today" {
		t.Errorf("search for 50%% should be literal, got %v", got)
	}
	if got := list(map[string]any{"search": "save_5"}); len(got) != 0 {
		t.Errorf("underscore should not be a wildcard, got %v", got)
	}
	if got := list(map[string]any{}); strings.Join(got, "|") != "under_score|save 500 today|save 50% today" {
		t.Errorf("default order should be newest first, got %v", got)
	}
	if got := list(map[string]any{"sortBy": "from", "sortDirection": "asc"}); strings.Join(got, "|") != "save 500 today|save 50% today|under_score" {
		t.Errorf("sort by from asc, got %v", got)
	}
	if got := list(map[string]any{"sortBy": "m.Id; DROP TABLE Mail", "sortDirection": "sideways"}); len(got) != 3 {
		t.Errorf("unknown sort should fall back to the default, got %v", got)
	}
}

func TestWebhookCaptureLimits(t *testing.T) {
	e := newTestEnv(t)
	admin, _ := e.login("admin", "admin")

	if w := e.do("POST", "/api/hook/"+strings.Repeat("a", 65), "", []byte("{}")); w.Code != 400 {
		t.Errorf("over-long bucket name: got %d, want 400", w.Code)
	}
	if w := e.do("POST", "/api/hook/bad%20name", "", []byte("{}")); w.Code != 400 {
		t.Errorf("bucket name with a space: got %d, want 400", w.Code)
	}

	e.srv.maxWebhooksPerBucket = 3
	for i := 0; i < 5; i++ {
		if w := e.do("POST", "/api/hook/keep", "", []byte(`{"n":`+itoa64(int64(i))+`}`)); w.Code != 200 {
			t.Fatalf("capture %d: %d", i, w.Code)
		}
	}
	var page pagedResults[Webhook]
	_ = json.Unmarshal(e.do("GET", "/api/webhooks/buckets/keep/webhooks", admin, nil).Body.Bytes(), &page)
	if page.TotalCount != 3 || len(page.Items) != 3 || page.Items[0].Body != `{"n":4}` || page.Items[2].Body != `{"n":2}` {
		t.Errorf("bucket should keep the newest 3 captures, got total %d %+v", page.TotalCount, page.Items)
	}

	e.srv.maxWebhookBuckets = 1
	if w := e.do("POST", "/api/hook/another", "", []byte("{}")); w.Code != http.StatusForbidden {
		t.Errorf("bucket over the limit: got %d, want 403", w.Code)
	}
	if w := e.do("POST", "/api/hook/keep", "", []byte("{}")); w.Code != 200 {
		t.Errorf("existing bucket at the limit should still capture, got %d", w.Code)
	}
}

func TestSecurityHeaders(t *testing.T) {
	e := newTestEnv(t)
	for _, path := range []string{"/", "/api/health"} {
		w := e.do("GET", path, "", nil)
		if got := w.Header().Get("X-Frame-Options"); got != "DENY" {
			t.Errorf("%s X-Frame-Options = %q", path, got)
		}
		if got := w.Header().Get("Content-Security-Policy"); !strings.Contains(got, "frame-ancestors 'none'") {
			t.Errorf("%s CSP = %q", path, got)
		}
		if got := w.Header().Get("Strict-Transport-Security"); got != "" {
			t.Errorf("%s plain HTTP should not get HSTS, got %q", path, got)
		}
	}
	// httptest's peer is 192.0.2.1 (not a trusted proxy), so a spoofed header is ignored.
	if w := e.do("GET", "/api/health", "", nil, "X-Forwarded-Proto", "https"); w.Header().Get("Strict-Transport-Security") != "" {
		t.Error("X-Forwarded-Proto from an untrusted peer should not enable HSTS")
	}
}

func TestRefreshTokenCleanup(t *testing.T) {
	e := newTestEnv(t)
	_, live := e.login("admin", "admin")
	now := time.Now().UTC()
	admin, _ := e.srv.userByName(context.Background(), "admin")
	for _, row := range []struct {
		token   string
		expiry  time.Time
		revoked int
		created time.Time
	}{
		{"expired", now.Add(-time.Hour), 0, now.AddDate(0, 0, -31)},
		{"revoked-old", now.AddDate(0, 0, 20), 1, now.AddDate(0, 0, -2)},
		{"revoked-new", now.AddDate(0, 0, 20), 1, now},
	} {
		if _, err := e.srv.db.Exec(`INSERT INTO RefreshToken (Token, UserId, ExpiryDate, IsRevoked, CreatedDate) VALUES (?, ?, ?, ?, ?)`,
			row.token, admin.ID, DBTime{row.expiry}, row.revoked, DBTime{row.created}); err != nil {
			t.Fatal(err)
		}
	}
	e.srv.cleanupRefreshTokens(context.Background())
	rows, _ := e.srv.db.Query(`SELECT Token FROM RefreshToken ORDER BY Token`)
	var left []string
	for rows.Next() {
		var s string
		_ = rows.Scan(&s)
		left = append(left, s)
	}
	rows.Close()
	if len(left) != 2 || !(left[0] == live || left[1] == live) || !(left[0] == "revoked-new" || left[1] == "revoked-new") {
		t.Errorf("cleanup should keep only the live and recently revoked tokens, got %v", left)
	}
}
