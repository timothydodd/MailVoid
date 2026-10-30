package api

import (
	"encoding/json"
	"net/http"
	"strings"
	"testing"
	"time"
)

func TestPersonalAPIKeys(t *testing.T) {
	e := newTestEnv(t)
	admin, _ := e.login("admin", "admin")
	e.createUser(admin, "alice")
	alice, _ := e.login("alice", "password1")

	// Alice makes a key; the plaintext comes back once and is never stored.
	w := e.do("POST", "/api/apikeys", alice, map[string]string{"name": "ci"})
	if w.Code != http.StatusCreated {
		t.Fatalf("create key: %d %s", w.Code, w.Body)
	}
	var created createAPIKeyResponse
	_ = json.Unmarshal(w.Body.Bytes(), &created)
	if !strings.HasPrefix(created.Key, apiKeyPrefix) || created.Prefix != created.Key[:apiKeyDisplay] || created.Name != "ci" {
		t.Fatalf("unexpected key response: %+v", created)
	}
	var stored string
	if err := e.srv.db.QueryRow(`SELECT KeyHash FROM UserApiKey WHERE Id = ?`, created.ID).Scan(&stored); err != nil || stored == created.Key || stored != hashAPIKey(created.Key) {
		t.Fatalf("key must be stored hashed: %q %v", stored, err)
	}

	// Listing never includes the plaintext.
	w = e.do("GET", "/api/apikeys", alice, nil)
	if w.Code != 200 || strings.Contains(w.Body.String(), created.Key) || !strings.Contains(w.Body.String(), `"prefix":"`+created.Prefix+`"`) {
		t.Fatalf("list keys: %d %s", w.Code, w.Body)
	}

	// v1 without a key, with a bogus key, and with a session token (not accepted there).
	for name, hdr := range map[string][]string{
		"none":   nil,
		"bogus":  {"X-Api-Key", apiKeyPrefix + "nope"},
		"bearer": {"Authorization", "Bearer " + alice},
	} {
		if w := e.do("GET", "/api/v1/mail", "", nil, hdr...); w.Code != http.StatusUnauthorized {
			t.Errorf("%s: got %d, want 401", name, w.Code)
		}
	}

	// Mail alice can see (her own group) vs. mail she can't (admin's private group).
	if w := e.do("POST", "/api/mail/mail-groups", alice, map[string]any{"subdomain": "team-a", "isPublic": false}); w.Code != 200 {
		t.Fatalf("create group: %d %s", w.Code, w.Body)
	}
	e.insertMailInGroup("me@alice.test", "team-a", "hello alice", time.Now().UTC())
	e.insertMailInGroup("someone@admin.test", "admin", "admin only", time.Now().UTC())

	list := func(hdr ...string) pagedResults[mailWithReadStatus] {
		w := e.do("GET", "/api/v1/mail?pageSize=10", "", nil, hdr...)
		if w.Code != 200 {
			t.Fatalf("v1 mail: %d %s", w.Code, w.Body)
		}
		var page pagedResults[mailWithReadStatus]
		_ = json.Unmarshal(w.Body.Bytes(), &page)
		return page
	}
	for name, hdr := range map[string][]string{
		"X-Api-Key":     {"X-Api-Key", created.Key},
		"Bearer mv_":    {"Authorization", "Bearer " + created.Key},
		"ApiKey scheme": {"Authorization", "ApiKey " + created.Key},
	} {
		page := list(hdr...)
		if page.TotalCount != 1 || len(page.Items) != 1 || page.Items[0].Subject != "hello alice" {
			t.Errorf("%s: key should see only alice's mail, got %+v", name, page)
		}
	}
	page := list("X-Api-Key", created.Key)
	if w := e.do("GET", "/api/v1/mail/"+itoa64(page.Items[0].ID), "", nil, "X-Api-Key", created.Key); w.Code != 200 {
		t.Errorf("v1 mail detail: %d %s", w.Code, w.Body)
	}
	var adminOnly int64
	_ = e.srv.db.QueryRow(`SELECT Id FROM Mail WHERE Subject = 'admin only'`).Scan(&adminOnly)
	if w := e.do("GET", "/api/v1/mail/"+itoa64(adminOnly), "", nil, "X-Api-Key", created.Key); w.Code != http.StatusNotFound {
		t.Errorf("v1 mail detail of invisible mail: got %d, want 404", w.Code)
	}
	if w := e.do("GET", "/api/v1/mail/boxes", "", nil, "X-Api-Key", created.Key); w.Code != 200 || !strings.Contains(w.Body.String(), "me@alice.test") {
		t.Errorf("v1 boxes: %d %s", w.Code, w.Body)
	}

	// Webhooks: a normal user needs an admin to enable the feature first (403 until then,
	// for both the session and the key), then public auto-created buckets are readable.
	if w := e.do("POST", "/api/hook/ci-bucket", "", []byte(`{"ok":true}`)); w.Code != 200 {
		t.Fatalf("capture: %d %s", w.Code, w.Body)
	}
	aliceID := e.userID("alice")
	for name, tok := range map[string]string{"session": alice, "key": ""} {
		hdr := []string{}
		if tok == "" {
			hdr = []string{"X-Api-Key", created.Key}
		}
		path := "/api/webhooks/buckets"
		if tok == "" {
			path = "/api/v1/webhooks/buckets"
		}
		if w := e.do("GET", path, tok, nil, hdr...); w.Code != http.StatusForbidden {
			t.Errorf("%s webhooks before enabling: got %d, want 403", name, w.Code)
		}
	}
	if w := e.do("PUT", "/api/UserManagement/"+aliceID.String()+"/webhooks", alice, map[string]bool{"enabled": true}); w.Code != http.StatusForbidden {
		t.Errorf("non-admin enabling webhooks: got %d, want 403", w.Code)
	}
	if w := e.do("PUT", "/api/UserManagement/"+aliceID.String()+"/webhooks", admin, map[string]bool{"enabled": true}); w.Code != http.StatusNoContent {
		t.Fatalf("enable webhooks: %d %s", w.Code, w.Body)
	}
	if w := e.do("GET", "/api/auth/user", alice, nil); w.Code != 200 || !strings.Contains(w.Body.String(), `"webhooksEnabled":true`) {
		t.Errorf("auth/user should report webhooksEnabled: %d %s", w.Code, w.Body)
	}
	if w := e.do("GET", "/api/UserManagement", admin, nil); !strings.Contains(w.Body.String(), `"webhooksEnabled":true`) {
		t.Errorf("user list should report webhooksEnabled: %s", w.Body)
	}
	if w := e.do("GET", "/api/webhooks/buckets", alice, nil); w.Code != 200 {
		t.Errorf("session webhooks after enabling: %d %s", w.Code, w.Body)
	}
	w = e.do("GET", "/api/v1/webhooks/buckets/ci-bucket/webhooks", "", nil, "X-Api-Key", created.Key)
	var hooks pagedResults[Webhook]
	_ = json.Unmarshal(w.Body.Bytes(), &hooks)
	if w.Code != 200 || hooks.TotalCount != 1 {
		t.Fatalf("v1 webhooks: %d %s", w.Code, w.Body)
	}
	if w := e.do("GET", "/api/v1/webhooks/"+itoa64(hooks.Items[0].ID), "", nil, "X-Api-Key", created.Key); w.Code != 200 || !strings.Contains(w.Body.String(), `{\"ok\":true}`) {
		t.Errorf("v1 webhook detail: %d %s", w.Code, w.Body)
	}
	if w := e.do("GET", "/api/v1/webhooks/buckets", "", nil, "X-Api-Key", created.Key); w.Code != 200 || !strings.Contains(w.Body.String(), "ci-bucket") {
		t.Errorf("v1 buckets: %d %s", w.Code, w.Body)
	}

	// The key is read-only: no DELETE route exists under /api/v1, and it can't be used
	// against the session-authenticated routes.
	if w := e.do("DELETE", "/api/v1/webhooks/"+itoa64(hooks.Items[0].ID), "", nil, "X-Api-Key", created.Key); w.Code != http.StatusMethodNotAllowed && w.Code != http.StatusNotFound {
		t.Errorf("v1 delete: got %d, want 405/404", w.Code)
	}
	if w := e.do("DELETE", "/api/webhooks/"+itoa64(hooks.Items[0].ID), "", nil, "X-Api-Key", created.Key); w.Code != http.StatusUnauthorized {
		t.Errorf("api key on session route: got %d, want 401", w.Code)
	}

	// Disabling takes effect immediately.
	if w := e.do("PUT", "/api/UserManagement/"+aliceID.String()+"/webhooks", admin, map[string]bool{"enabled": false}); w.Code != http.StatusNoContent {
		t.Fatalf("disable webhooks: %d %s", w.Code, w.Body)
	}
	if w := e.do("GET", "/api/webhooks/buckets", alice, nil); w.Code != http.StatusForbidden {
		t.Errorf("session webhooks after disabling: got %d, want 403", w.Code)
	}

	// LastUsedAt is recorded.
	var lastUsed NullTime
	_ = e.srv.db.QueryRow(`SELECT LastUsedAt FROM UserApiKey WHERE Id = ?`, created.ID).Scan(&lastUsed)
	if !lastUsed.Valid {
		t.Error("LastUsedAt should be set after use")
	}

	// Another user can't delete alice's key; alice can, and it stops working at once.
	adminKey := "/api/apikeys/" + itoa64(created.ID)
	if w := e.do("DELETE", adminKey, admin, nil); w.Code != http.StatusNotFound {
		t.Errorf("delete other user's key: got %d, want 404", w.Code)
	}
	if w := e.do("DELETE", adminKey, alice, nil); w.Code != http.StatusNoContent {
		t.Errorf("delete own key: got %d", w.Code)
	}
	if w := e.do("GET", "/api/v1/mail", "", nil, "X-Api-Key", created.Key); w.Code != http.StatusUnauthorized {
		t.Errorf("deleted key should be rejected, got %d", w.Code)
	}
}

func (e *testEnv) insertMailInGroup(to, groupSubdomain, subject string, created time.Time) {
	e.t.Helper()
	var path string
	if err := e.srv.db.QueryRow(`SELECT Path FROM MailGroup WHERE Subdomain = ?`, groupSubdomain).Scan(&path); err != nil {
		e.t.Fatalf("group %s: %v", groupSubdomain, err)
	}
	_, err := e.srv.db.Exec(`INSERT INTO Mail ([To], Text, IsHtml, [From], Subject, CreatedOn, MailGroupPath) VALUES (?, '', 0, 'x@test', ?, ?, ?)`,
		to, subject, DBTime{created}, path)
	if err != nil {
		e.t.Fatal(err)
	}
}

func (e *testEnv) userID(name string) GUID {
	e.t.Helper()
	var id GUID
	if err := e.srv.db.QueryRow(`SELECT Id FROM [User] WHERE UserName = ?`, name).Scan(&id); err != nil {
		e.t.Fatal(err)
	}
	return id
}
