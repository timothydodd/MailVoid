package api

import (
	"context"
	"crypto/rand"
	"crypto/sha256"
	"database/sql"
	"encoding/base64"
	"encoding/hex"
	"errors"
	"net/http"
	"strconv"
	"strings"
	"time"
)

// ---- Personal API keys ----
//
// A user generates a key in the UI and uses it against the read-only /api/v1 endpoints,
// which return exactly what that user sees in the web app (same mail visibility and
// webhook bucket rules). The plaintext key is shown once; only its SHA-256 is stored.

const (
	apiKeyPrefix   = "mv_"
	apiKeyDisplay  = 11 // "mv_" + 8 characters, enough to tell keys apart in a list
	maxAPIKeyName  = 100
	maxAPIKeysUser = 25
	// LastUsedAt is written at most this often per key so polling clients don't turn
	// every read into a write.
	apiKeyLastUsedResolution = time.Minute
)

type UserAPIKey struct {
	ID         int64    `json:"id"`
	UserID     GUID     `json:"-"`
	Name       string   `json:"name"`
	KeyHash    string   `json:"-"`
	Prefix     string   `json:"prefix"`
	CreatedAt  DBTime   `json:"createdAt"`
	LastUsedAt NullTime `json:"lastUsedAt"`
}

const userAPIKeyCols = `Id, UserId, Name, KeyHash, Prefix, CreatedAt, LastUsedAt`

func scanUserAPIKey(row interface{ Scan(...any) error }) (*UserAPIKey, error) {
	var k UserAPIKey
	if err := row.Scan(&k.ID, &k.UserID, &k.Name, &k.KeyHash, &k.Prefix, &k.CreatedAt, &k.LastUsedAt); err != nil {
		return nil, err
	}
	return &k, nil
}

// newUserAPIKey returns a fresh plaintext key: a recognisable prefix plus 256 bits of
// randomness in URL-safe base64, so it is safe in headers and query strings.
func newUserAPIKey() string {
	b := make([]byte, 32)
	_, _ = rand.Read(b)
	return apiKeyPrefix + base64.RawURLEncoding.EncodeToString(b)
}

func hashAPIKey(key string) string {
	sum := sha256.Sum256([]byte(key))
	return hex.EncodeToString(sum[:])
}

func (s *Server) handleListAPIKeys(w http.ResponseWriter, r *http.Request, p *Principal) {
	keys, err := queryAll(r.Context(), s.db, scanUserAPIKey,
		`SELECT `+userAPIKeyCols+` FROM UserApiKey WHERE UserId = ? ORDER BY CreatedAt DESC, Id DESC`, p.UserID)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	out := make([]UserAPIKey, 0, len(keys))
	for _, k := range keys {
		out = append(out, *k)
	}
	writeJSON(w, http.StatusOK, out)
}

type createAPIKeyRequest struct {
	Name string `json:"name"`
}

type createAPIKeyResponse struct {
	UserAPIKey
	// Key is the plaintext, returned only from this call.
	Key string `json:"key"`
}

func (s *Server) handleCreateAPIKey(w http.ResponseWriter, r *http.Request, p *Principal) {
	var req createAPIKeyRequest
	if err := decodeBody(r, &req, true); err != nil {
		writeBodyError(w, err)
		return
	}
	name := strings.TrimSpace(req.Name)
	if name == "" {
		name = "API key"
	}
	if len([]rune(name)) > maxAPIKeyName {
		writeText(w, http.StatusBadRequest, "Name must be at most 100 characters.")
		return
	}
	ctx := r.Context()
	n, err := scalarInt(ctx, s.db, `SELECT COUNT(*) FROM UserApiKey WHERE UserId = ?`, p.UserID)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if n >= maxAPIKeysUser {
		writeText(w, http.StatusBadRequest, "You have reached the maximum number of API keys. Delete one first.")
		return
	}

	key := newUserAPIKey()
	k := UserAPIKey{UserID: p.UserID, Name: name, KeyHash: hashAPIKey(key), Prefix: key[:apiKeyDisplay], CreatedAt: Now()}
	res, err := s.db.ExecContext(ctx, `INSERT INTO UserApiKey (UserId, Name, KeyHash, Prefix, CreatedAt, LastUsedAt) VALUES (?, ?, ?, ?, ?, NULL)`,
		k.UserID, k.Name, k.KeyHash, k.Prefix, k.CreatedAt)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	k.ID, _ = res.LastInsertId()
	s.log.Info("created API key", "user", p.UserName, "keyId", k.ID)
	writeJSON(w, http.StatusCreated, createAPIKeyResponse{UserAPIKey: k, Key: key})
}

func (s *Server) handleDeleteAPIKey(w http.ResponseWriter, r *http.Request, p *Principal) {
	id, ok := pathInt64(w, r, "id")
	if !ok {
		return
	}
	// Scoped to the caller, so another user's key id is simply "not found".
	res, err := s.db.ExecContext(r.Context(), `DELETE FROM UserApiKey WHERE Id = ? AND UserId = ?`, id, p.UserID)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if n, _ := res.RowsAffected(); n == 0 {
		writeProblem(w, http.StatusNotFound)
		return
	}
	s.log.Info("deleted API key", "user", p.UserName, "keyId", id)
	w.WriteHeader(http.StatusNoContent)
}

// ---- Authenticating with a personal API key ----

// apiKeyFromRequest accepts the same spellings as the mail-server webhook key, plus
// "Authorization: Bearer mv_..." for clients that only know how to send bearer tokens.
func apiKeyFromRequest(r *http.Request) string {
	if v := r.Header.Get("X-Api-Key"); v != "" {
		return strings.TrimSpace(v)
	}
	if h := r.Header.Get("Authorization"); len(h) > 7 {
		scheme, rest := h[:7], strings.TrimSpace(h[7:])
		if strings.EqualFold(scheme, "ApiKey ") || (strings.EqualFold(scheme, "Bearer ") && strings.HasPrefix(rest, apiKeyPrefix)) {
			return rest
		}
	}
	return strings.TrimSpace(r.URL.Query().Get("api_key"))
}

// authenticateAPIKey resolves a personal API key into the owning user's Principal.
// Unknown keys and deleted users yield nil.
func (s *Server) authenticateAPIKey(ctx context.Context, key string) (*Principal, error) {
	if !strings.HasPrefix(key, apiKeyPrefix) {
		return nil, nil
	}
	var p Principal
	var sub *string
	var role Role
	var keyID int64
	var lastUsed NullTime
	err := s.db.QueryRowContext(ctx, `SELECT k.Id, k.LastUsedAt, u.Id, u.UserName, u.Role, u.Subdomain
		FROM UserApiKey k JOIN [User] u ON u.Id = k.UserId WHERE k.KeyHash = ?`, hashAPIKey(key)).
		Scan(&keyID, &lastUsed, &p.UserID, &p.UserName, &role, &sub)
	if errors.Is(err, sql.ErrNoRows) {
		return nil, nil
	}
	if err != nil {
		return nil, err
	}
	p.Role = role.String()
	if sub != nil {
		p.Subdomain = strings.TrimSpace(*sub)
	}
	if now := time.Now(); !lastUsed.Valid || now.Sub(lastUsed.Time) >= apiKeyLastUsedResolution {
		if _, err := s.db.ExecContext(ctx, `UPDATE UserApiKey SET LastUsedAt = ? WHERE Id = ?`, NullTimeOf(now), keyID); err != nil {
			s.log.Warn("failed to record API key use", "keyId", keyID, "error", err)
		}
	}
	return &p, nil
}

// requireUserAPIKey guards the /api/v1 endpoints: a valid personal API key is mapped to
// its owner, and the wrapped handler runs exactly as it would for that user's session.
func (s *Server) requireUserAPIKey(next func(http.ResponseWriter, *http.Request, *Principal)) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		var p *Principal
		if key := apiKeyFromRequest(r); key != "" {
			var err error
			if p, err = s.authenticateAPIKey(r.Context(), key); err != nil {
				s.internalError(w, r, err)
				return
			}
		}
		if p == nil {
			w.Header().Set("WWW-Authenticate", `ApiKey realm="mailvoid", Bearer`)
			writeMessage(w, http.StatusUnauthorized, "A valid API key is required (X-Api-Key header).")
			return
		}
		next(w, r.WithContext(context.WithValue(r.Context(), principalKey, p)), p)
	}
}

// ---- /api/v1 read-only endpoints ----

// handleV1GetMails is the GET form of POST /api/mail: filters come from the query string
// (to, search, page, pageSize, sortBy, sortDirection) and the response shape is identical.
func (s *Server) handleV1GetMails(w http.ResponseWriter, r *http.Request, p *Principal) {
	q := r.URL.Query()
	opts := filterOptions{Page: 1, PageSize: 50}
	optStr := func(name string) *string {
		if v := strings.TrimSpace(q.Get(name)); v != "" {
			return &v
		}
		return nil
	}
	for name, dst := range map[string]*int{"page": &opts.Page, "pageSize": &opts.PageSize} {
		if v := q.Get(name); v != "" {
			n, err := strconv.Atoi(v)
			if err != nil {
				writeText(w, http.StatusBadRequest, name+" must be an integer")
				return
			}
			*dst = n
		}
	}
	opts.To, opts.Search = optStr("to"), optStr("search")
	opts.SortBy, opts.SortDirection = optStr("sortBy"), optStr("sortDirection")
	result, err := s.listMails(r.Context(), p, opts)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	writeJSON(w, http.StatusOK, result)
}
