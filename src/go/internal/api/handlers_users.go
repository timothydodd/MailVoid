package api

import (
	"context"
	"encoding/json"
	"fmt"
	"net/http"
	"strings"
)

// userDto is UserManagementController's DTO; Id is Guid.ToString() (lower-case).
type userDto struct {
	ID        string  `json:"id"`
	UserName  string  `json:"userName"`
	Role      Role    `json:"role"`
	TimeStamp DBTime  `json:"timeStamp"`
	Subdomain *string `json:"subdomain"`
	// WebhooksEnabled reports whether the user may use webhook capture (always true for admins).
	WebhooksEnabled bool `json:"webhooksEnabled"`
}

func toUserDto(u *User, webhooksEnabled bool) userDto {
	return userDto{ID: u.ID.String(), UserName: u.UserName, Role: u.Role, TimeStamp: u.TimeStamp, Subdomain: u.Subdomain,
		WebhooksEnabled: u.Role == RoleAdmin || webhooksEnabled}
}

// webhookAccessSet returns the ids of users explicitly granted webhook access.
func (s *Server) webhookAccessSet(ctx context.Context) (map[GUID]bool, error) {
	rows, err := s.db.QueryContext(ctx, `SELECT UserId FROM UserWebhookAccess`)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	set := map[GUID]bool{}
	for rows.Next() {
		var id GUID
		if err := rows.Scan(&id); err != nil {
			return nil, err
		}
		set[id] = true
	}
	return set, rows.Err()
}

// minPasswordLength applies to new passwords only; existing ones (including the seeded
// admin/admin) keep working until they are changed.
const minPasswordLength = 8

// passwordProblem returns a user-facing message when a new password is unacceptable.
func passwordProblem(pw string) string {
	if len([]rune(pw)) < minPasswordLength {
		return fmt.Sprintf("Password must be at least %d characters.", minPasswordLength)
	}
	return ""
}

// isUserAdmin is the controller's exact "Admin" role check.
func isUserAdmin(p *Principal) bool { return p.Role == RoleAdmin.String() }

func (s *Server) handleListUsers(w http.ResponseWriter, r *http.Request, p *Principal) {
	if !isUserAdmin(p) {
		forbid(w)
		return
	}
	users, err := queryAll(r.Context(), s.db, scanUser, `SELECT `+userCols+` FROM [User]`)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	access, err := s.webhookAccessSet(r.Context())
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	out := make([]userDto, 0, len(users))
	for _, u := range users {
		out = append(out, toUserDto(u, access[u.ID]))
	}
	writeJSON(w, http.StatusOK, out)
}

func (s *Server) pathUserID(w http.ResponseWriter, r *http.Request) (GUID, bool) {
	id, ok := ParseGUID(r.PathValue("id"))
	if !ok {
		writeProblem(w, http.StatusBadRequest)
	}
	return id, ok
}

func (s *Server) handleGetUserByID(w http.ResponseWriter, r *http.Request, p *Principal) {
	id, ok := s.pathUserID(w, r)
	if !ok {
		return
	}
	if !isUserAdmin(p) && !p.UserID.Equal(id) {
		forbid(w)
		return
	}
	u, err := s.userByID(r.Context(), id)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if u == nil {
		writeProblem(w, http.StatusNotFound)
		return
	}
	access, err := s.webhookAccessSet(r.Context())
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	writeJSON(w, http.StatusOK, toUserDto(u, access[u.ID]))
}

func (s *Server) handleCreateUser(w http.ResponseWriter, r *http.Request, p *Principal) {
	if !isUserAdmin(p) {
		forbid(w)
		return
	}
	var req struct {
		UserName  string  `json:"userName"`
		Password  string  `json:"password"`
		Role      Role    `json:"role"`
		Subdomain *string `json:"subdomain"`
	}
	if err := decodeBody(r, &req, false); err != nil {
		writeBodyError(w, err)
		return
	}
	if strings.TrimSpace(req.UserName) == "" || strings.TrimSpace(req.Password) == "" {
		writeText(w, http.StatusBadRequest, "Username and password are required.")
		return
	}
	if msg := passwordProblem(req.Password); msg != "" {
		writeText(w, http.StatusBadRequest, msg)
		return
	}
	ctx := r.Context()
	if existing, err := s.userByName(ctx, req.UserName); err != nil {
		s.internalError(w, r, err)
		return
	} else if existing != nil {
		writeText(w, http.StatusBadRequest, "Username already exists.")
		return
	}

	var subdomain *string
	if req.Subdomain != nil && strings.TrimSpace(*req.Subdomain) != "" {
		sd := strings.ToLower(strings.TrimSpace(*req.Subdomain))
		n, err := scalarInt(ctx, s.db, `SELECT COUNT(*) FROM [User] WHERE Subdomain = ?`, sd)
		if err != nil {
			s.internalError(w, r, err)
			return
		}
		if n > 0 {
			writeText(w, http.StatusBadRequest, "Subdomain is already assigned to another user.")
			return
		}
		subdomain = &sd
	} else if req.Subdomain != nil {
		// Whitespace-only subdomains were stored untouched by the .NET service.
		subdomain = req.Subdomain
	}

	u := &User{ID: NewGUID(), UserName: req.UserName, PasswordHash: hashPassword(req.Password), TimeStamp: Now(), Role: req.Role, Subdomain: subdomain}
	if _, err := s.db.ExecContext(ctx, `INSERT INTO [User] (Id, UserName, PasswordHash, TimeStamp, Role, Subdomain) VALUES (?, ?, ?, ?, ?, ?)`,
		u.ID, u.UserName, u.PasswordHash, u.TimeStamp, u.Role, u.Subdomain); err != nil {
		s.internalError(w, r, err)
		return
	}
	if err := s.createUserPrivateMailGroup(ctx, u.ID, true); err != nil {
		s.internalError(w, r, err)
		return
	}
	w.Header().Set("Location", "/api/UserManagement/"+u.ID.String())
	writeJSON(w, http.StatusCreated, toUserDto(u, false))
}

func (s *Server) handleUpdateUserRole(w http.ResponseWriter, r *http.Request, p *Principal) {
	if !isUserAdmin(p) {
		forbid(w)
		return
	}
	id, ok := s.pathUserID(w, r)
	if !ok {
		return
	}
	var req struct {
		Role Role `json:"role"`
	}
	if err := decodeBody(r, &req, false); err != nil {
		writeBodyError(w, err)
		return
	}
	res, err := s.db.ExecContext(r.Context(), `UPDATE [User] SET Role = ? WHERE Id = ?`, req.Role, id)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if n, _ := res.RowsAffected(); n == 0 {
		writeProblem(w, http.StatusNotFound)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

// handleUpdateUserWebhooks lets an admin enable or disable the webhook capture feature
// for a non-admin user. Admins always have it, so toggling one is a no-op.
func (s *Server) handleUpdateUserWebhooks(w http.ResponseWriter, r *http.Request, p *Principal) {
	if !isUserAdmin(p) {
		forbid(w)
		return
	}
	id, ok := s.pathUserID(w, r)
	if !ok {
		return
	}
	var req struct {
		Enabled bool `json:"enabled"`
	}
	if err := decodeBody(r, &req, false); err != nil {
		writeBodyError(w, err)
		return
	}
	ctx := r.Context()
	u, err := s.userByID(ctx, id)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if u == nil {
		writeProblem(w, http.StatusNotFound)
		return
	}
	if req.Enabled {
		_, err = s.db.ExecContext(ctx, `INSERT OR IGNORE INTO UserWebhookAccess (UserId, GrantedAt) VALUES (?, ?)`, id, Now())
	} else {
		_, err = s.db.ExecContext(ctx, `DELETE FROM UserWebhookAccess WHERE UserId = ?`, id)
	}
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	s.log.Info("updated webhook access", "user", u.UserName, "enabled", req.Enabled, "by", p.UserName)
	w.WriteHeader(http.StatusNoContent)
}

func (s *Server) handleUpdateUserPassword(w http.ResponseWriter, r *http.Request, p *Principal) {
	id, ok := s.pathUserID(w, r)
	if !ok {
		return
	}
	if !isUserAdmin(p) && !p.UserID.Equal(id) {
		forbid(w)
		return
	}
	var req struct {
		NewPassword string `json:"newPassword"`
	}
	if err := decodeBody(r, &req, false); err != nil {
		writeBodyError(w, err)
		return
	}
	if strings.TrimSpace(req.NewPassword) == "" {
		writeText(w, http.StatusBadRequest, "New password is required.")
		return
	}
	if msg := passwordProblem(req.NewPassword); msg != "" {
		writeText(w, http.StatusBadRequest, msg)
		return
	}
	res, err := s.db.ExecContext(r.Context(), `UPDATE [User] SET PasswordHash = ? WHERE Id = ?`, hashPassword(req.NewPassword), id)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if n, _ := res.RowsAffected(); n == 0 {
		writeProblem(w, http.StatusNotFound)
		return
	}
	// A password reset signs the user out everywhere.
	if _, err := s.db.ExecContext(r.Context(), `UPDATE RefreshToken SET IsRevoked = 1 WHERE UserId = ? AND IsRevoked = 0`, id); err != nil {
		s.internalError(w, r, err)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

func (s *Server) handleDeleteUser(w http.ResponseWriter, r *http.Request, p *Principal) {
	if !isUserAdmin(p) {
		forbid(w)
		return
	}
	id, ok := s.pathUserID(w, r)
	if !ok {
		return
	}
	if p.UserID.Equal(id) {
		writeText(w, http.StatusBadRequest, "Cannot delete your own account.")
		return
	}
	// Owned mail groups, grants, refresh tokens and read markers cascade.
	res, err := s.db.ExecContext(r.Context(), `DELETE FROM [User] WHERE Id = ?`, id)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if n, _ := res.RowsAffected(); n == 0 {
		writeProblem(w, http.StatusNotFound)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

// handleHealth reproduces the HealthCheck.WriteResponse JSON for the SQLite check. A
// failing database reports Degraded (HTTP 200), as configured in .NET.
func (s *Server) handleHealth(w http.ResponseWriter, r *http.Request) {
	status := "Healthy"
	var desc any
	var n int
	if err := s.db.QueryRowContext(r.Context(), `SELECT 1`).Scan(&n); err != nil {
		// The endpoint is anonymous, so log the detail rather than returning it.
		s.log.Error("health check failed", "error", err)
		status = "Degraded"
		desc = "Database unavailable"
	}
	body, _ := json.MarshalIndent(map[string]any{
		"status": status,
		"results": map[string]any{
			"SQLITE DB": map[string]any{"status": status, "description": desc, "data": map[string]any{}},
		},
	}, "", "  ")
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(http.StatusOK)
	_, _ = w.Write(body)
}
