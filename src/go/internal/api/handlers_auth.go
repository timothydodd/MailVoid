package api

import (
	"context"
	"net/http"
	"time"
)

// The refresh token travels as an httpOnly cookie scoped to /api/auth, exactly as before.
const (
	refreshCookieName = "refreshToken"
	refreshCookiePath = "/api/auth"
)

func (s *Server) setRefreshCookie(w http.ResponseWriter, token string, expires time.Time) {
	http.SetCookie(w, &http.Cookie{
		Name:     refreshCookieName,
		Value:    token,
		Path:     refreshCookiePath,
		Expires:  expires,
		HttpOnly: true,
		Secure:   s.jwt.CookieSecure,
		SameSite: s.jwt.CookieSameSite,
	})
}

func (s *Server) deleteRefreshCookie(w http.ResponseWriter) {
	s.setRefreshCookie(w, "", time.Unix(0, 0))
}

type loginResponse struct {
	AccessToken string `json:"accessToken"`
	ExpiresIn   int    `json:"expiresIn"`
}

// dummyPasswordHash lets failed logins for unknown users cost the same PBKDF2 work as
// real ones, so response time doesn't reveal which usernames exist.
var dummyPasswordHash = hashPassword("mailvoid-timing-equalizer")

func (s *Server) userByID(ctx context.Context, id GUID) (*User, error) {
	return queryOne(ctx, s.db, scanUser, `SELECT `+userCols+` FROM [User] WHERE Id = ?`, id)
}

func (s *Server) userByName(ctx context.Context, name string) (*User, error) {
	return queryOne(ctx, s.db, scanUser, `SELECT `+userCols+` FROM [User] WHERE UserName = ?`, name)
}

func (s *Server) createRefreshToken(ctx context.Context, userID GUID) (string, error) {
	token := generateRefreshToken()
	now := time.Now().UTC()
	_, err := s.db.ExecContext(ctx,
		`INSERT INTO RefreshToken (Token, UserId, ExpiryDate, IsRevoked, CreatedDate) VALUES (?, ?, ?, 0, ?)`,
		token, userID, DBTime{now.AddDate(0, 0, s.jwt.RefreshTokenExpiryDays)}, DBTime{now})
	return token, err
}

// activeRefreshToken returns a non-revoked token row (GetRefreshTokenAsync).
func (s *Server) activeRefreshToken(ctx context.Context, token string) (*RefreshToken, error) {
	return queryOne(ctx, s.db, func(row interface{ Scan(...any) error }) (*RefreshToken, error) {
		var t RefreshToken
		err := row.Scan(&t.ID, &t.Token, &t.UserID, &t.ExpiryDate, &t.IsRevoked, &t.CreatedDate)
		return &t, err
	}, `SELECT Id, Token, UserId, ExpiryDate, IsRevoked, CreatedDate FROM RefreshToken WHERE Token = ? AND IsRevoked = 0 LIMIT 1`, token)
}

func (s *Server) issueTokens(w http.ResponseWriter, r *http.Request, u *User) {
	access, err := s.generateJWT(u)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	refresh, err := s.createRefreshToken(r.Context(), u.ID)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	s.setRefreshCookie(w, refresh, time.Now().UTC().AddDate(0, 0, s.jwt.RefreshTokenExpiryDays))
	writeJSON(w, http.StatusOK, loginResponse{AccessToken: access, ExpiresIn: s.jwt.ExpiryMinutes * 60})
}

func (s *Server) handleLogin(w http.ResponseWriter, r *http.Request) {
	var req struct {
		UserName string `json:"userName"`
		Password string `json:"password"`
	}
	if err := decodeBody(r, &req, false); err != nil {
		writeBodyError(w, err)
		return
	}
	u, err := s.userByName(r.Context(), req.UserName)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if u == nil {
		verifyPassword(dummyPasswordHash, req.Password)
		writeText(w, http.StatusUnauthorized, "Invalid email or password.")
		return
	}
	if !verifyPassword(u.PasswordHash, req.Password) {
		writeText(w, http.StatusUnauthorized, "Invalid email or password.")
		return
	}
	s.issueTokens(w, r, u)
}

func (s *Server) handleGetUser(w http.ResponseWriter, r *http.Request, p *Principal) {
	u, err := s.userByID(r.Context(), p.UserID)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if u == nil {
		writeProblem(w, http.StatusNotFound)
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{"id": u.ID, "userName": u.UserName})
}

func (s *Server) handleChangePassword(w http.ResponseWriter, r *http.Request, p *Principal) {
	var req struct {
		OldPassword string `json:"oldPassword"`
		NewPassword string `json:"newPassword"`
	}
	if err := decodeBody(r, &req, false); err != nil {
		writeBodyError(w, err)
		return
	}
	u, err := s.userByID(r.Context(), p.UserID)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if u == nil {
		writeProblem(w, http.StatusNotFound)
		return
	}
	if !verifyPassword(u.PasswordHash, req.OldPassword) {
		writeText(w, http.StatusUnauthorized, "Invalid password.")
		return
	}
	if _, err := s.db.ExecContext(r.Context(), `UPDATE [User] SET PasswordHash = ? WHERE Id = ?`, hashPassword(req.NewPassword), u.ID); err != nil {
		s.internalError(w, r, err)
		return
	}
	// Sign out every other session; the one changing the password stays logged in.
	current := ""
	if c, err := r.Cookie(refreshCookieName); err == nil {
		current = c.Value
	}
	if _, err := s.db.ExecContext(r.Context(), `UPDATE RefreshToken SET IsRevoked = 1 WHERE UserId = ? AND IsRevoked = 0 AND Token <> ?`, u.ID, current); err != nil {
		s.internalError(w, r, err)
		return
	}
	w.WriteHeader(http.StatusOK)
}

func (s *Server) handleRefresh(w http.ResponseWriter, r *http.Request) {
	var req struct {
		AccessToken  string `json:"accessToken"`
		RefreshToken string `json:"refreshToken"`
	}
	if err := decodeBody(r, &req, false); err != nil {
		writeBodyError(w, err)
		return
	}
	p, err := s.parseJWT(req.AccessToken, false)
	if err != nil {
		writeText(w, http.StatusBadRequest, "Invalid access token")
		return
	}

	// Prefer the httpOnly cookie; fall back to the body for transitional clients.
	value := ""
	if c, err := r.Cookie(refreshCookieName); err == nil {
		value = c.Value
	}
	if value == "" {
		value = req.RefreshToken
	}
	if value == "" {
		writeText(w, http.StatusUnauthorized, "Invalid refresh token")
		return
	}

	stored, err := s.activeRefreshToken(r.Context(), value)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	// The refresh token must be live and bound to the user in the (expired) access token.
	if stored == nil || !stored.ExpiryDate.After(time.Now().UTC()) || !stored.UserID.Equal(p.UserID) {
		writeText(w, http.StatusUnauthorized, "Invalid refresh token")
		return
	}

	u, err := s.userByID(r.Context(), p.UserID)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if u == nil {
		writeText(w, http.StatusNotFound, "User not found")
		return
	}

	// Rotate: revoke the presented token and issue a new pair. Only the request whose
	// UPDATE actually revoked it may continue, so a token can't be redeemed twice.
	res, err := s.db.ExecContext(r.Context(), `UPDATE RefreshToken SET IsRevoked = 1 WHERE Token = ? AND IsRevoked = 0`, value)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if n, _ := res.RowsAffected(); n == 0 {
		writeText(w, http.StatusUnauthorized, "Invalid refresh token")
		return
	}
	s.issueTokens(w, r, u)
}

func (s *Server) handleRevoke(w http.ResponseWriter, r *http.Request, p *Principal) {
	var req struct {
		RefreshToken string `json:"refreshToken"`
	}
	_ = decodeBody(r, &req, true)

	value := ""
	if c, err := r.Cookie(refreshCookieName); err == nil {
		value = c.Value
	}
	if value == "" {
		value = req.RefreshToken
	}
	if value == "" {
		writeText(w, http.StatusBadRequest, "No refresh token supplied")
		return
	}

	// Users may only revoke their own tokens.
	stored, err := s.activeRefreshToken(r.Context(), value)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if stored != nil && !stored.UserID.Equal(p.UserID) {
		forbid(w)
		return
	}
	if _, err := s.db.ExecContext(r.Context(), `UPDATE RefreshToken SET IsRevoked = 1 WHERE Token = ?`, value); err != nil {
		s.internalError(w, r, err)
		return
	}
	s.deleteRefreshCookie(w)
	w.WriteHeader(http.StatusOK)
}

func (s *Server) handleLogout(w http.ResponseWriter, r *http.Request, p *Principal) {
	if _, err := s.db.ExecContext(r.Context(), `UPDATE RefreshToken SET IsRevoked = 1 WHERE UserId = ? AND IsRevoked = 0`, p.UserID); err != nil {
		s.internalError(w, r, err)
		return
	}
	s.deleteRefreshCookie(w)
	w.WriteHeader(http.StatusOK)
}
