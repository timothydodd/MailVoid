package api

import (
	"context"
	"crypto/hmac"
	"crypto/pbkdf2"
	"crypto/rand"
	"crypto/sha1"
	"crypto/sha256"
	"crypto/sha512"
	"crypto/subtle"
	"database/sql"
	"encoding/base64"
	"encoding/binary"
	"errors"
	"hash"
	"net/http"
	"strings"
	"time"

	"github.com/golang-jwt/jwt/v5"
)

// ---- Password hashing (ASP.NET Core Identity PasswordHasher<TUser> formats) ----

// hashPassword produces an Identity V3 hash: PBKDF2-HMAC-SHA512, 100k iterations,
// 128-bit salt, 256-bit subkey, i.e. the .NET 8+ default.
func hashPassword(password string) string {
	const iterations, saltLen, subkeyLen = 100000, 16, 32
	salt := make([]byte, saltLen)
	_, _ = rand.Read(salt)
	subkey, _ := pbkdf2.Key(sha512.New, password, salt, iterations, subkeyLen)

	out := make([]byte, 13+saltLen+subkeyLen)
	out[0] = 0x01
	binary.BigEndian.PutUint32(out[1:], 2) // KeyDerivationPrf.HMACSHA512
	binary.BigEndian.PutUint32(out[5:], iterations)
	binary.BigEndian.PutUint32(out[9:], saltLen)
	copy(out[13:], salt)
	copy(out[13+saltLen:], subkey)
	return base64.StdEncoding.EncodeToString(out)
}

// verifyPassword accepts both Identity V3 (0x01) and legacy V2 (0x00) hashes.
func verifyPassword(encoded, password string) bool {
	raw, err := base64.StdEncoding.DecodeString(encoded)
	if err != nil || len(raw) == 0 {
		return false
	}
	switch raw[0] {
	case 0x00: // V2: PBKDF2-HMAC-SHA1, 1000 iterations, 128-bit salt, 256-bit subkey
		if len(raw) != 1+16+32 {
			return false
		}
		actual, err := pbkdf2.Key(sha1.New, password, raw[1:17], 1000, 32)
		return err == nil && subtle.ConstantTimeCompare(actual, raw[17:]) == 1
	case 0x01:
		if len(raw) < 13 {
			return false
		}
		var prf func() hash.Hash
		switch binary.BigEndian.Uint32(raw[1:]) {
		case 0:
			prf = sha1.New
		case 1:
			prf = sha256.New
		case 2:
			prf = sha512.New
		default:
			return false
		}
		iterations := int(binary.BigEndian.Uint32(raw[5:]))
		saltLen := int(binary.BigEndian.Uint32(raw[9:]))
		if saltLen < 16 || 13+saltLen > len(raw) {
			return false
		}
		salt := raw[13 : 13+saltLen]
		expected := raw[13+saltLen:]
		if len(expected) < 16 {
			return false
		}
		actual, err := pbkdf2.Key(prf, password, salt, iterations, len(expected))
		return err == nil && hmac.Equal(actual, expected)
	}
	return false
}

// ---- JWT ----

// Claim types as written by the .NET API. Outbound claim mapping was disabled there, so
// the long WS-Federation URIs appear verbatim in the token (the SPA reads them).
const (
	claimNameIdentifier = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"
	claimName           = "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"
	claimRole           = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
)

type JWTSettings struct {
	Secret                 string
	Issuer                 string
	Audience               string
	ExpiryMinutes          int
	RefreshTokenExpiryDays int
	CookieSameSite         http.SameSite
	CookieSecure           bool
}

type Principal struct {
	UserID    GUID
	UserName  string
	Role      string
	Subdomain string
}

// IsAdmin mirrors UserService.IsAdmin (case-insensitive role check).
func (p *Principal) IsAdmin() bool { return strings.EqualFold(p.Role, "admin") }

func (s *Server) generateJWT(u *User) (string, error) {
	now := time.Now().UTC()
	id := u.ID.String()
	claims := jwt.MapClaims{
		"sub":               id,
		"jti":               NewGUID().String(),
		"iat":               now.Unix(),
		claimNameIdentifier: id,
		claimName:           u.UserName,
		claimRole:           u.Role.String(),
		"nbf":               now.Unix(),
		"exp":               now.Add(time.Duration(s.jwt.ExpiryMinutes) * time.Minute).Unix(),
		"iss":               s.jwt.Issuer,
		"aud":               s.jwt.Audience,
	}
	if u.Subdomain != nil && *u.Subdomain != "" {
		claims["subdomain"] = *u.Subdomain
	}
	return jwt.NewWithClaims(jwt.SigningMethodHS256, claims).SignedString([]byte(s.jwt.Secret))
}

// parseJWT validates signature, algorithm, issuer and audience. Lifetime is checked with
// the same 5 minute clock skew as the .NET TokenValidationParameters unless
// validateLifetime is false (used by the refresh endpoint).
func (s *Server) parseJWT(tokenString string, validateLifetime bool) (*Principal, error) {
	if strings.TrimSpace(tokenString) == "" {
		return nil, errors.New("empty token")
	}
	opts := []jwt.ParserOption{
		jwt.WithValidMethods([]string{"HS256"}),
		jwt.WithIssuer(s.jwt.Issuer),
		jwt.WithAudience(s.jwt.Audience),
		jwt.WithExpirationRequired(),
		jwt.WithLeeway(5 * time.Minute),
	}
	if !validateLifetime {
		opts = append(opts, jwt.WithoutClaimsValidation())
	}
	claims := jwt.MapClaims{}
	tok, err := jwt.ParseWithClaims(tokenString, claims, func(*jwt.Token) (any, error) {
		return []byte(s.jwt.Secret), nil
	}, opts...)
	if err != nil {
		return nil, err
	}
	if typ, _ := tok.Header["typ"].(string); typ != "JWT" {
		return nil, errors.New("unexpected token type")
	}
	if !validateLifetime {
		// WithoutClaimsValidation skips iss/aud too; the refresh path still requires them.
		if iss, _ := claims.GetIssuer(); iss != s.jwt.Issuer {
			return nil, errors.New("invalid issuer")
		}
		aud, _ := claims.GetAudience()
		found := false
		for _, a := range aud {
			found = found || a == s.jwt.Audience
		}
		if !found {
			return nil, errors.New("invalid audience")
		}
		if exp, _ := claims.GetExpirationTime(); exp == nil {
			return nil, errors.New("missing exp")
		}
	}

	claimString := func(k string) string { v, _ := claims[k].(string); return v }
	p := &Principal{
		UserName:  claimString(claimName),
		Role:      claimString(claimRole),
		Subdomain: claimString("subdomain"),
	}
	if p.Role == "" {
		p.Role = claimString("role")
	}
	sub := claimString("sub")
	if sub == "" {
		sub = claimString(claimNameIdentifier)
	}
	id, ok := ParseGUID(sub)
	if !ok {
		return nil, errors.New("token has no valid user id")
	}
	p.UserID = id
	return p, nil
}

func generateRefreshToken() string {
	b := make([]byte, 64)
	_, _ = rand.Read(b)
	return base64.StdEncoding.EncodeToString(b)
}

// ---- Request authentication ----

type ctxKey int

const principalKey ctxKey = 0

func principalFrom(r *http.Request) *Principal {
	p, _ := r.Context().Value(principalKey).(*Principal)
	return p
}

// authenticate resolves the bearer token (or, for /hubs, the access_token query value
// used by the SignalR client) into a Principal. Missing/invalid tokens yield nil.
func (s *Server) authenticate(r *http.Request) *Principal {
	token := ""
	if h := r.Header.Get("Authorization"); len(h) > 7 && strings.EqualFold(h[:7], "Bearer ") {
		token = strings.TrimSpace(h[7:])
	}
	if token == "" && strings.HasPrefix(r.URL.Path, "/hubs") {
		token = r.URL.Query().Get("access_token")
	}
	if token == "" {
		return nil
	}
	p, err := s.parseJWT(token, true)
	if err != nil {
		s.log.Debug("JWT authentication failed", "path", r.URL.Path, "error", err)
		return nil
	}
	return p
}

// requireAuth is the equivalent of [Authorize]: 401 with a Bearer challenge when the
// request carries no valid token.
func (s *Server) requireAuth(next func(http.ResponseWriter, *http.Request, *Principal)) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		p := s.authenticate(r)
		if p != nil {
			// The token alone isn't trusted for who the user is now: a deleted user is
			// rejected and a role or subdomain change applies immediately rather than when
			// the access token expires. One primary-key lookup per request.
			ok, err := s.refreshPrincipal(r.Context(), p)
			if err != nil {
				s.internalError(w, r, err)
				return
			}
			if !ok {
				p = nil
			}
		}
		if p == nil {
			w.Header().Set("WWW-Authenticate", "Bearer")
			w.WriteHeader(http.StatusUnauthorized)
			return
		}
		next(w, r.WithContext(context.WithValue(r.Context(), principalKey, p)), p)
	}
}

// refreshPrincipal overwrites the token's role and subdomain with the user's current
// values. It reports false when the user no longer exists.
func (s *Server) refreshPrincipal(ctx context.Context, p *Principal) (bool, error) {
	var role Role
	var sub *string
	err := s.db.QueryRowContext(ctx, `SELECT Role, Subdomain FROM [User] WHERE Id = ?`, p.UserID).Scan(&role, &sub)
	if errors.Is(err, sql.ErrNoRows) {
		return false, nil
	}
	if err != nil {
		return false, err
	}
	p.Role = role.String()
	p.Subdomain = ""
	if sub != nil {
		p.Subdomain = strings.TrimSpace(*sub)
	}
	return true, nil
}

type APIKey struct {
	Name    string
	Key     string
	Enabled bool
	Scopes  []string
}

// requireAPIKey is the equivalent of the [ApiKey] policy: the key may arrive in the
// X-Api-Key header, the api_key query value, or "Authorization: ApiKey <key>".
func (s *Server) requireAPIKey(next http.HandlerFunc) http.HandlerFunc {
	return func(w http.ResponseWriter, r *http.Request) {
		provided, present := "", false
		if v, ok := r.Header["X-Api-Key"]; ok {
			provided, present = strings.Join(v, ","), true
		} else if v, ok := r.URL.Query()["api_key"]; ok {
			if len(v) > 0 {
				provided = v[0]
			}
			present = true
		} else if h := r.Header.Get("Authorization"); len(h) > 7 && strings.EqualFold(h[:7], "ApiKey ") {
			provided, present = strings.TrimSpace(h[7:]), true
		}

		if present && provided != "" {
			for _, k := range s.apiKeys {
				if k.Enabled && k.Key != "" && subtle.ConstantTimeCompare([]byte(k.Key), []byte(provided)) == 1 {
					next(w, r)
					return
				}
			}
		}
		w.WriteHeader(http.StatusUnauthorized)
	}
}
