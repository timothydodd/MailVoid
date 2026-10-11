package api

import (
	"context"
	"database/sql"
	"fmt"
	"net/http"
	"regexp"
	"strconv"
	"strings"
)

// mailVisibilityClause is the shared visibility predicate from MailController. It needs a
// Mail alias `m` LEFT JOINed to MailGroup `mg` and @UserId / @UserSubdomain / @IsAdmin.
// Admins additionally see orphaned mail (mg.Id IS NULL).
const mailVisibilityClause = `(
        mg.OwnerUserId = @UserId
        OR mg.Subdomain = @UserSubdomain
        OR EXISTS (SELECT 1 FROM MailGroupUser mgu WHERE mgu.MailGroupId = mg.Id AND mgu.UserId = @UserId)
        OR (@IsAdmin = 1 AND mg.Subdomain = 'default')
        OR (@IsAdmin = 1 AND mg.Id IS NULL)
    )`

const notHiddenClause = `NOT EXISTS (SELECT 1 FROM HiddenMailGroup h WHERE h.UserId = @UserId AND h.MailGroupId = mg.Id)`

// visibilityArgs binds the visibility parameters. A missing subdomain claim binds NULL
// (so it never matches), as the .NET code passed a null string.
func visibilityArgs(p *Principal) []any {
	var sub any
	if p.Subdomain != "" {
		sub = p.Subdomain
	}
	admin := 0
	if p.IsAdmin() {
		admin = 1
	}
	return []any{sql.Named("UserId", p.UserID), sql.Named("UserSubdomain", sub), sql.Named("IsAdmin", admin)}
}

var validSubdomain = regexp.MustCompile(`^[a-z0-9]([a-z0-9-]*[a-z0-9])?$`)

// canManageGroup: changing a group's sharing, settings or retention is limited to its
// owner and admins. (The .NET API also let any user manage public groups.)
func canManageGroup(g *MailGroup, p *Principal) bool {
	return p.IsAdmin() || g.OwnerUserID.Equal(p.UserID)
}

func pathInt64(w http.ResponseWriter, r *http.Request, name string) (int64, bool) {
	n, err := strconv.ParseInt(r.PathValue(name), 10, 64)
	if err != nil {
		writeProblem(w, http.StatusBadRequest)
		return 0, false
	}
	return n, true
}

func (s *Server) canAccessMail(ctx context.Context, p *Principal, mailID int64) (bool, error) {
	args := append(visibilityArgs(p), sql.Named("MailId", mailID))
	n, err := scalarInt(ctx, s.db, `SELECT COUNT(*) FROM Mail m LEFT JOIN MailGroup mg ON m.MailGroupPath = mg.Path
		WHERE m.Id = @MailId AND `+mailVisibilityClause, args...)
	return n > 0, err
}

func (s *Server) canAccessMailbox(ctx context.Context, p *Principal, to string) (bool, error) {
	args := append(visibilityArgs(p), sql.Named("To", to))
	n, err := scalarInt(ctx, s.db, `SELECT COUNT(*) FROM Mail m LEFT JOIN MailGroup mg ON m.MailGroupPath = mg.Path
		WHERE m.[To] = @To AND `+mailVisibilityClause, args...)
	return n > 0, err
}

func (s *Server) handleGetMail(w http.ResponseWriter, r *http.Request, p *Principal) {
	id, ok := pathInt64(w, r, "id")
	if !ok {
		return
	}
	ctx := r.Context()
	mail, err := queryOne(ctx, s.db, scanMail, `SELECT `+mailCols+` FROM Mail m WHERE m.Id = ?`, id)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if mail == nil {
		writeProblem(w, http.StatusNotFound)
		return
	}
	// NotFound rather than Forbid so the existence of the mail isn't disclosed.
	if allowed, err := s.canAccessMail(ctx, p, id); err != nil {
		s.internalError(w, r, err)
		return
	} else if !allowed {
		writeProblem(w, http.StatusNotFound)
		return
	}

	if markAsRead, _ := strconv.ParseBool(r.URL.Query().Get("markAsRead")); markAsRead {
		if _, err := s.db.ExecContext(ctx, `INSERT OR IGNORE INTO UserMailRead (UserId, MailId, ReadAt) VALUES (?, ?, ?)`,
			p.UserID, id, Now()); err != nil {
			s.internalError(w, r, err)
			return
		}
	}
	writeJSON(w, http.StatusOK, mail)
}

type mailBox struct {
	Path        *string `json:"path"`
	Name        string  `json:"name"`
	MailBoxName *string `json:"mailBoxName"`
	IsPublic    bool    `json:"isPublic"`
	IsOwner     bool    `json:"isOwner"`
	UnreadCount int     `json:"unreadCount"`
}

func (s *Server) handleGetBoxes(w http.ResponseWriter, r *http.Request, p *Principal) {
	ctx := r.Context()
	rows, err := s.db.QueryContext(ctx, `SELECT DISTINCT m.[To], m.MailGroupPath, mg.Subdomain, mg.IsPublic, mg.OwnerUserId
		FROM Mail m
		LEFT JOIN MailGroup mg ON m.MailGroupPath = mg.Path
		WHERE `+mailVisibilityClause+`
		AND `+notHiddenClause, visibilityArgs(p)...)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	result := []mailBox{}
	for rows.Next() {
		var b mailBox
		var isPublic sql.NullInt64
		var owner *string
		if err := rows.Scan(&b.Name, &b.Path, &b.MailBoxName, &isPublic, &owner); err != nil {
			rows.Close()
			s.internalError(w, r, err)
			return
		}
		b.IsPublic = isPublic.Valid && isPublic.Int64 != 0
		b.IsOwner = owner != nil && GUID(*owner).Equal(p.UserID)
		result = append(result, b)
	}
	rows.Close()
	if err := rows.Err(); err != nil {
		s.internalError(w, r, err)
		return
	}

	// One GROUP BY for unread counts instead of a query per mailbox.
	unread := map[string]int{}
	// Limited to mail the user can see, so it doesn't scan the whole table.
	urows, err := s.db.QueryContext(ctx, `SELECT m.[To] AS ToAddress, COUNT(*) AS UnreadCount FROM Mail m
		LEFT JOIN MailGroup mg ON m.MailGroupPath = mg.Path
		WHERE `+mailVisibilityClause+`
		AND NOT EXISTS (SELECT 1 FROM UserMailRead umr WHERE umr.MailId = m.Id AND umr.UserId = @UserId)
		GROUP BY m.[To]`, visibilityArgs(p)...)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	for urows.Next() {
		var to string
		var n int
		if err := urows.Scan(&to, &n); err != nil {
			urows.Close()
			s.internalError(w, r, err)
			return
		}
		unread[to] = n
	}
	urows.Close()
	for i := range result {
		result[i].UnreadCount = unread[result[i].Name]
	}
	writeJSON(w, http.StatusOK, result)
}

type filterOptions struct {
	Page     int     `json:"page"`
	PageSize int     `json:"pageSize"`
	To       *string `json:"to"`
	Search   *string `json:"search"`
}

type mailWithReadStatus struct {
	ID            int64   `json:"id"`
	To            string  `json:"to"`
	From          string  `json:"from"`
	Subject       string  `json:"subject"`
	Text          *string `json:"text"`
	HTML          *string `json:"html"`
	CreatedOn     DBTime  `json:"createdOn"`
	MailGroupPath *string `json:"mailGroupPath"`
	IsRead        bool    `json:"isRead"`
}

func scanMailListItem(row interface{ Scan(...any) error }) (*Mail, error) {
	var m Mail
	if err := row.Scan(&m.ID, &m.To, &m.From, &m.Subject, &m.Text, &m.IsHTML, &m.CreatedOn, &m.MailGroupPath); err != nil {
		return nil, err
	}
	return &m, nil
}

type pagedResults[T any] struct {
	Items      []T   `json:"items"`
	TotalCount int64 `json:"totalCount"`
}

func (s *Server) handleGetMails(w http.ResponseWriter, r *http.Request, p *Principal) {
	opts := filterOptions{Page: 1, PageSize: 50}
	if err := decodeBody(r, &opts, true); err != nil {
		writeBodyError(w, err)
		return
	}
	if opts.Page < 1 {
		opts.Page = 1
	}
	opts.PageSize = max(1, min(opts.PageSize, 200))

	args := visibilityArgs(p)
	var where []string
	if opts.To != nil && *opts.To != "" {
		where = append(where, "m.[To] = @To")
		args = append(args, sql.Named("To", *opts.To))
	}
	if opts.Search != nil && *opts.Search != "" {
		where = append(where, "(m.Subject LIKE @Search OR m.[From] LIKE @Search OR m.[To] LIKE @Search OR m.Text LIKE @Search)")
		args = append(args, sql.Named("Search", "%"+strings.TrimSpace(*opts.Search)+"%"))
	}
	where = append(where, mailVisibilityClause, notHiddenClause)
	whereClause := "WHERE " + strings.Join(where, " AND ")
	from := `FROM Mail m LEFT JOIN MailGroup mg ON m.MailGroupPath = mg.Path ` + whereClause

	ctx := r.Context()
	total, err := scalarInt(ctx, s.db, `SELECT COUNT(*) `+from, args...)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	pageArgs := append(args, sql.Named("Limit", opts.PageSize), sql.Named("Offset", (opts.Page-1)*opts.PageSize))
	// Only the columns the list returns; raw source, headers and attachments can be large.
	mails, err := queryAll(ctx, s.db, scanMailListItem, `SELECT m.Id, m.[To], m.[From], m.Subject, m.Text, m.IsHtml, m.CreatedOn, m.MailGroupPath `+
		from+` ORDER BY m.CreatedOn DESC LIMIT @Limit OFFSET @Offset`, pageArgs...)
	if err != nil {
		s.internalError(w, r, err)
		return
	}

	read := map[int64]bool{}
	if len(mails) > 0 {
		ids := make([]string, len(mails))
		for i, m := range mails {
			ids[i] = strconv.FormatInt(m.ID, 10)
		}
		rows, err := s.db.QueryContext(ctx, `SELECT MailId FROM UserMailRead WHERE UserId = ? AND MailId IN (`+strings.Join(ids, ",")+`)`, p.UserID)
		if err != nil {
			s.internalError(w, r, err)
			return
		}
		for rows.Next() {
			var id int64
			if err := rows.Scan(&id); err == nil {
				read[id] = true
			}
		}
		rows.Close()
	}

	items := make([]mailWithReadStatus, 0, len(mails))
	for _, m := range mails {
		item := mailWithReadStatus{ID: m.ID, To: m.To, From: m.From, Subject: m.Subject, CreatedOn: m.CreatedOn,
			MailGroupPath: m.MailGroupPath, IsRead: read[m.ID]}
		text := m.Text
		if m.IsHTML {
			item.HTML = &text
		} else {
			item.Text = &text
		}
		items = append(items, item)
	}
	writeJSON(w, http.StatusOK, pagedResults[mailWithReadStatus]{Items: items, TotalCount: total})
}

func (s *Server) handleDeleteBox(w http.ResponseWriter, r *http.Request, p *Principal) {
	var opts filterOptions
	if err := decodeBody(r, &opts, false); err != nil || opts.To == nil || *opts.To == "" {
		writeBodyError(w, err)
		return
	}
	ctx := r.Context()
	n, err := scalarInt(ctx, s.db, `SELECT COUNT(*) FROM Mail WHERE [To] = ?`, *opts.To)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if n == 0 {
		w.WriteHeader(http.StatusOK)
		return
	}
	if allowed, err := s.canAccessMailbox(ctx, p, *opts.To); err != nil {
		s.internalError(w, r, err)
		return
	} else if !allowed {
		forbid(w)
		return
	}
	// UserMailRead rows go with the declared ON DELETE CASCADE.
	if _, err := s.db.ExecContext(ctx, `DELETE FROM Mail WHERE [To] = ?`, *opts.To); err != nil {
		s.internalError(w, r, err)
		return
	}
	w.WriteHeader(http.StatusOK)
}

type mailGroupView struct {
	ID            int64    `json:"id"`
	Path          string   `json:"path"`
	Subdomain     string   `json:"subdomain"`
	Description   string   `json:"description"`
	IsPublic      bool     `json:"isPublic"`
	CreatedAt     DBTime   `json:"createdAt"`
	LastActivity  NullTime `json:"lastActivity"`
	IsOwner       bool     `json:"isOwner"`
	IsUserPrivate bool     `json:"isUserPrivate"`
}

func (s *Server) handleGetMailGroups(w http.ResponseWriter, r *http.Request, p *Principal) {
	type view struct {
		mailGroupView
		IsHidden bool `json:"isHidden"`
	}
	rows, err := s.db.QueryContext(r.Context(), `SELECT mg.Id, mg.Path, mg.Subdomain, mg.Description, mg.IsPublic, mg.CreatedAt, mg.LastActivity, mg.OwnerUserId, mg.IsUserPrivate,
			EXISTS (SELECT 1 FROM HiddenMailGroup h WHERE h.UserId = @UserId AND h.MailGroupId = mg.Id) AS IsHidden
		FROM MailGroup mg
		WHERE mg.Subdomain IS NOT NULL
		AND (
			(mg.IsUserPrivate = 1 AND mg.OwnerUserId = @UserId)
			OR (mg.IsUserPrivate = 0 AND (
				mg.OwnerUserId = @UserId
				OR mg.Subdomain = @UserSubdomain
				OR EXISTS (SELECT 1 FROM MailGroupUser mgu WHERE mgu.MailGroupId = mg.Id AND mgu.UserId = @UserId)
				OR (@IsAdmin = 1 AND mg.Subdomain = 'default')
			))
		)`, visibilityArgs(p)...)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	defer rows.Close()
	result := []view{}
	for rows.Next() {
		var v view
		var path, sub, desc *string
		var owner GUID
		if err := rows.Scan(&v.ID, &path, &sub, &desc, &v.IsPublic, &v.CreatedAt, &v.LastActivity, &owner, &v.IsUserPrivate, &v.IsHidden); err != nil {
			s.internalError(w, r, err)
			return
		}
		v.Path, v.Subdomain, v.Description = deref(path), deref(sub), deref(desc)
		v.IsOwner = owner.Equal(p.UserID)
		result = append(result, v)
	}
	writeJSON(w, http.StatusOK, result)
}

func (s *Server) handleHideMailGroup(w http.ResponseWriter, r *http.Request, p *Principal) {
	id, ok := pathInt64(w, r, "id")
	if !ok {
		return
	}
	if g, err := s.mailGroupByID(r.Context(), id); err != nil {
		s.internalError(w, r, err)
		return
	} else if g == nil {
		writeProblem(w, http.StatusNotFound)
		return
	}
	if _, err := s.db.ExecContext(r.Context(), `INSERT OR IGNORE INTO HiddenMailGroup (UserId, MailGroupId, CreatedAt) VALUES (?, ?, ?)`,
		p.UserID, id, Now()); err != nil {
		s.internalError(w, r, err)
		return
	}
	w.WriteHeader(http.StatusOK)
}

func (s *Server) handleUnhideMailGroup(w http.ResponseWriter, r *http.Request, p *Principal) {
	id, ok := pathInt64(w, r, "id")
	if !ok {
		return
	}
	if _, err := s.db.ExecContext(r.Context(), `DELETE FROM HiddenMailGroup WHERE UserId = ? AND MailGroupId = ?`, p.UserID, id); err != nil {
		s.internalError(w, r, err)
		return
	}
	w.WriteHeader(http.StatusOK)
}

func (s *Server) handleGetAllMailGroups(w http.ResponseWriter, r *http.Request, p *Principal) {
	if !p.IsAdmin() {
		forbid(w)
		return
	}
	type view struct {
		mailGroupView
		OwnerUserName string `json:"ownerUserName"`
		HasAccess     bool   `json:"hasAccess"`
	}
	rows, err := s.db.QueryContext(r.Context(), `SELECT mg.Id, mg.Path, mg.Subdomain, mg.Description, mg.IsPublic, mg.CreatedAt, mg.LastActivity, mg.OwnerUserId, mg.IsUserPrivate,
			u.UserName as OwnerUserName,
			EXISTS (SELECT 1 FROM MailGroupUser mgu WHERE mgu.MailGroupId = mg.Id AND mgu.UserId = @UserId) as HasAccess
		FROM MailGroup mg
		LEFT JOIN [User] u ON mg.OwnerUserId = u.Id
		WHERE mg.Subdomain IS NOT NULL AND mg.IsUserPrivate = 0`, sql.Named("UserId", p.UserID))
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	defer rows.Close()
	result := []view{}
	for rows.Next() {
		var v view
		var path, sub, desc, ownerName *string
		var owner GUID
		if err := rows.Scan(&v.ID, &path, &sub, &desc, &v.IsPublic, &v.CreatedAt, &v.LastActivity, &owner, &v.IsUserPrivate, &ownerName, &v.HasAccess); err != nil {
			s.internalError(w, r, err)
			return
		}
		v.Path, v.Subdomain, v.Description, v.OwnerUserName = deref(path), deref(sub), deref(desc), deref(ownerName)
		v.IsOwner = owner.Equal(p.UserID)
		v.HasAccess = v.HasAccess || v.IsOwner
		result = append(result, v)
	}
	writeJSON(w, http.StatusOK, result)
}

func (s *Server) handleGrantAccess(w http.ResponseWriter, r *http.Request, p *Principal) {
	id, ok := pathInt64(w, r, "id")
	if !ok {
		return
	}
	var req struct {
		UserID GUID `json:"userId"`
	}
	if err := decodeBody(r, &req, false); err != nil || req.UserID == "" {
		writeBodyError(w, err)
		return
	}
	ctx := r.Context()
	g, err := s.mailGroupByID(ctx, id)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if g == nil {
		writeProblem(w, http.StatusNotFound)
		return
	}
	if !canManageGroup(g, p) {
		forbid(w)
		return
	}
	if g.IsUserPrivate {
		writeMessage(w, http.StatusBadRequest, "Private user mailboxes cannot be shared.")
		return
	}
	if target, err := s.userByID(ctx, req.UserID); err != nil {
		s.internalError(w, r, err)
		return
	} else if target == nil {
		writeProblem(w, http.StatusNotFound)
		return
	}
	exists, err := s.hasExplicitAccess(ctx, id, req.UserID)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if !exists {
		if _, err := s.db.ExecContext(ctx, `INSERT INTO MailGroupUser (MailGroupId, UserId, GrantedAt) VALUES (?, ?, ?)`, id, req.UserID, Now()); err != nil {
			s.internalError(w, r, err)
			return
		}
		s.log.Info("granted mail group access", "userId", req.UserID.String(), "mailGroupId", id)
	}
	w.WriteHeader(http.StatusOK)
}

func (s *Server) handleRevokeAccess(w http.ResponseWriter, r *http.Request, p *Principal) {
	id, ok := pathInt64(w, r, "id")
	if !ok {
		return
	}
	userID, ok := ParseGUID(r.PathValue("userId"))
	if !ok {
		writeProblem(w, http.StatusBadRequest)
		return
	}
	ctx := r.Context()
	g, err := s.mailGroupByID(ctx, id)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if g == nil {
		writeProblem(w, http.StatusNotFound)
		return
	}
	if !canManageGroup(g, p) {
		forbid(w)
		return
	}
	if g.IsUserPrivate {
		writeMessage(w, http.StatusBadRequest, "Private user mailboxes cannot have access modified.")
		return
	}
	res, err := s.db.ExecContext(ctx, `DELETE FROM MailGroupUser WHERE MailGroupId = ? AND UserId = ?`, id, userID)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if n, _ := res.RowsAffected(); n > 0 {
		s.log.Info("revoked mail group access", "userId", userID.String(), "mailGroupId", id)
	}
	w.WriteHeader(http.StatusOK)
}

func (s *Server) handleUpdateMailGroup(w http.ResponseWriter, r *http.Request, p *Principal) {
	id, ok := pathInt64(w, r, "id")
	if !ok {
		return
	}
	var req struct {
		Description *string `json:"description"`
		IsPublic    bool    `json:"isPublic"`
	}
	if err := decodeBody(r, &req, false); err != nil {
		writeBodyError(w, err)
		return
	}
	ctx := r.Context()
	g, err := s.mailGroupByID(ctx, id)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if g == nil {
		writeProblem(w, http.StatusNotFound)
		return
	}
	if !canManageGroup(g, p) {
		forbid(w)
		return
	}
	// Private mailboxes can only change their description.
	g.Description = req.Description
	if !g.IsUserPrivate {
		g.IsPublic = req.IsPublic
	}
	if _, err := s.db.ExecContext(ctx, `UPDATE MailGroup SET Description = ?, IsPublic = ? WHERE Id = ?`, g.Description, g.IsPublic, g.ID); err != nil {
		s.internalError(w, r, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{
		"id":            g.ID,
		"path":          deref(g.Path),
		"subdomain":     deref(g.Subdomain),
		"description":   deref(g.Description),
		"isPublic":      g.IsPublic,
		"createdAt":     g.CreatedAt,
		"isOwner":       g.OwnerUserID.Equal(p.UserID),
		"isUserPrivate": g.IsUserPrivate,
	})
}

func (s *Server) handleGetMailGroupUsers(w http.ResponseWriter, r *http.Request, p *Principal) {
	id, ok := pathInt64(w, r, "id")
	if !ok {
		return
	}
	ctx := r.Context()
	g, err := s.mailGroupByID(ctx, id)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if g == nil {
		writeProblem(w, http.StatusNotFound)
		return
	}
	// Members can see who else has access; being public alone isn't enough.
	if !canManageGroup(g, p) {
		member, err := s.hasExplicitAccess(ctx, id, p.UserID)
		if err != nil {
			s.internalError(w, r, err)
			return
		}
		if !member {
			forbid(w)
			return
		}
	}
	rows, err := s.db.QueryContext(ctx, `SELECT mgu.Id, mgu.MailGroupId, mgu.UserId, mgu.GrantedAt,
			u.Id, u.UserName, u.Role, u.TimeStamp
		FROM MailGroupUser mgu
		INNER JOIN [User] u ON mgu.UserId = u.Id
		WHERE mgu.MailGroupId = ?`, id)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	defer rows.Close()
	type userView struct {
		ID        GUID   `json:"id"`
		UserName  string `json:"userName"`
		Role      Role   `json:"role"`
		TimeStamp DBTime `json:"timeStamp"`
	}
	type view struct {
		ID          int64    `json:"id"`
		MailGroupID int64    `json:"mailGroupId"`
		UserID      GUID     `json:"userId"`
		GrantedAt   DBTime   `json:"grantedAt"`
		User        userView `json:"user"`
	}
	result := []view{}
	for rows.Next() {
		var v view
		if err := rows.Scan(&v.ID, &v.MailGroupID, &v.UserID, &v.GrantedAt, &v.User.ID, &v.User.UserName, &v.User.Role, &v.User.TimeStamp); err != nil {
			s.internalError(w, r, err)
			return
		}
		result = append(result, v)
	}
	writeJSON(w, http.StatusOK, result)
}

func (s *Server) handleGetUsers(w http.ResponseWriter, r *http.Request, p *Principal) {
	if !p.IsAdmin() {
		forbid(w)
		return
	}
	users, err := queryAll(r.Context(), s.db, scanUser, `SELECT `+userCols+` FROM [User]`)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if users == nil {
		users = []*User{}
	}
	writeJSON(w, http.StatusOK, users)
}

func (s *Server) handleCreateMailGroup(w http.ResponseWriter, r *http.Request, p *Principal) {
	var req struct {
		Subdomain   string  `json:"subdomain"`
		Description *string `json:"description"`
		IsPublic    bool    `json:"isPublic"`
	}
	if err := decodeBody(r, &req, false); err != nil || req.Subdomain == "" {
		writeBodyError(w, err)
		return
	}
	// Incoming mail is routed by the lower-cased first label of the recipient domain, so
	// store subdomains the same way or the group never receives anything.
	req.Subdomain = strings.ToLower(strings.TrimSpace(req.Subdomain))
	if !validSubdomain.MatchString(req.Subdomain) {
		writeMessage(w, http.StatusBadRequest, "Subdomain may only contain letters, numbers and hyphens.")
		return
	}
	ctx := r.Context()
	n, err := scalarInt(ctx, s.db, `SELECT COUNT(*) FROM MailGroup WHERE Subdomain = ? COLLATE NOCASE`, req.Subdomain)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if n > 0 {
		writeMessage(w, http.StatusBadRequest, "A mail group with this subdomain already exists.")
		return
	}
	desc := fmt.Sprintf("Mail group for %s", req.Subdomain)
	if req.Description != nil {
		desc = *req.Description
	}
	g := &MailGroup{
		Path:          strPtr(mailGroupPath(req.Subdomain)),
		Subdomain:     strPtr(req.Subdomain),
		Description:   &desc,
		OwnerUserID:   p.UserID,
		IsPublic:      req.IsPublic,
		CreatedAt:     Now(),
		RetentionDays: intPtr(3),
	}
	if err := s.insertMailGroup(ctx, g); err != nil {
		s.internalError(w, r, err)
		return
	}
	writeJSON(w, http.StatusOK, map[string]any{
		"id":          g.ID,
		"path":        g.Path,
		"subdomain":   g.Subdomain,
		"description": g.Description,
		"isPublic":    g.IsPublic,
		"createdAt":   g.CreatedAt,
	})
}

func (s *Server) handleDeleteMailGroup(w http.ResponseWriter, r *http.Request, p *Principal) {
	id, ok := pathInt64(w, r, "id")
	if !ok {
		return
	}
	ctx := r.Context()
	g, err := s.mailGroupByID(ctx, id)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if g == nil {
		writeProblem(w, http.StatusNotFound)
		return
	}
	if g.IsDefaultMailbox {
		writeMessage(w, http.StatusBadRequest, "Cannot delete default mailbox.")
		return
	}
	// User.IsInRole("Admin") is an exact role-claim match.
	if !g.OwnerUserID.Equal(p.UserID) && p.Role != "Admin" {
		forbid(w)
		return
	}
	if _, err := s.db.ExecContext(ctx, `DELETE FROM MailGroup WHERE Id = ?`, id); err != nil {
		s.internalError(w, r, err)
		return
	}
	w.WriteHeader(http.StatusOK)
}

type retentionResponse struct {
	MailGroupID   int64   `json:"mailGroupId"`
	RetentionDays *int    `json:"retentionDays"`
	Path          *string `json:"path"`
}

// retentionGroup loads a group and applies the retention endpoints' read rule (admin,
// public, owner, or explicit grant). It writes the error response itself.
func (s *Server) retentionGroup(w http.ResponseWriter, r *http.Request, p *Principal) *MailGroup {
	id, ok := pathInt64(w, r, "id")
	if !ok {
		return nil
	}
	ctx := r.Context()
	g, err := s.mailGroupByID(ctx, id)
	if err != nil {
		s.internalError(w, r, err)
		return nil
	}
	if g == nil {
		writeProblem(w, http.StatusNotFound)
		return nil
	}
	if !g.IsPublic && !canManageGroup(g, p) {
		explicit, err := s.hasExplicitAccess(ctx, id, p.UserID)
		if err != nil {
			s.internalError(w, r, err)
			return nil
		}
		if !explicit {
			forbid(w)
			return nil
		}
	}
	return g
}

func (s *Server) handleGetRetention(w http.ResponseWriter, r *http.Request, p *Principal) {
	g := s.retentionGroup(w, r, p)
	if g == nil {
		return
	}
	writeJSON(w, http.StatusOK, retentionResponse{MailGroupID: g.ID, RetentionDays: g.RetentionDays, Path: g.Path})
}

func (s *Server) handleUpdateRetention(w http.ResponseWriter, r *http.Request, p *Principal) {
	g := s.retentionGroup(w, r, p)
	if g == nil {
		return
	}
	// Retention deletes mail, so only the owner or an admin may change it.
	if !canManageGroup(g, p) {
		forbid(w)
		return
	}
	var req struct {
		RetentionDays *int `json:"retentionDays"`
	}
	if err := decodeBody(r, &req, false); err != nil {
		writeBodyError(w, err)
		return
	}
	if req.RetentionDays != nil && (*req.RetentionDays < 0 || *req.RetentionDays > 365) {
		writeMessage(w, http.StatusBadRequest, "Retention days must be between 0 and 365.")
		return
	}
	if _, err := s.db.ExecContext(r.Context(), `UPDATE MailGroup SET RetentionDays = ? WHERE Id = ?`, req.RetentionDays, g.ID); err != nil {
		s.internalError(w, r, err)
		return
	}
	g.RetentionDays = req.RetentionDays
	s.log.Info("updated mailbox retention", "path", deref(g.Path), "days", req.RetentionDays)
	writeJSON(w, http.StatusOK, retentionResponse{MailGroupID: g.ID, RetentionDays: g.RetentionDays, Path: g.Path})
}

func (s *Server) handleMarkAllAsRead(w http.ResponseWriter, r *http.Request, p *Principal) {
	var req struct {
		MailboxPath *string `json:"mailboxPath"`
	}
	if err := decodeBody(r, &req, false); err != nil {
		writeBodyError(w, err)
		return
	}
	ctx := r.Context()

	var candidates string
	var args []any
	if req.MailboxPath != nil && *req.MailboxPath != "" {
		g, err := queryOne(ctx, s.db, scanMailGroup, `SELECT `+mailGroupCols+` FROM MailGroup WHERE Path = ? LIMIT 1`, *req.MailboxPath)
		if err != nil {
			s.internalError(w, r, err)
			return
		}
		if g != nil && !g.IsPublic && !g.OwnerUserID.Equal(p.UserID) {
			explicit, err := s.hasExplicitAccess(ctx, g.ID, p.UserID)
			if err != nil {
				s.internalError(w, r, err)
				return
			}
			if !explicit {
				forbid(w)
				return
			}
		}
		candidates = `SELECT m.Id FROM Mail m WHERE m.MailGroupPath = @MailboxPath`
		args = []any{sql.Named("MailboxPath", *req.MailboxPath), sql.Named("UserId", p.UserID)}
	} else {
		// Only mail the user may see, never every row in the table.
		candidates = `SELECT m.Id FROM Mail m LEFT JOIN MailGroup mg ON m.MailGroupPath = mg.Path WHERE ` + mailVisibilityClause
		args = visibilityArgs(p)
	}

	total, err := scalarInt(ctx, s.db, `SELECT COUNT(*) FROM (`+candidates+`)`, args...)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if total == 0 {
		writeJSON(w, http.StatusOK, map[string]any{"message": "No emails found to mark as read.", "markedCount": 0})
		return
	}

	// INSERT OR IGNORE against the unique (UserId, MailId) index inserts exactly the
	// unread ones, so the affected row count is the number newly marked.
	res, err := s.db.ExecContext(ctx, `INSERT OR IGNORE INTO UserMailRead (UserId, MailId, ReadAt)
		SELECT @UserId, c.Id, @ReadAt FROM (`+candidates+`) c`, append(args, sql.Named("ReadAt", Now()))...)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	marked, _ := res.RowsAffected()
	if marked == 0 {
		writeJSON(w, http.StatusOK, map[string]any{"message": "All emails are already marked as read.", "markedCount": 0})
		return
	}
	mailbox := "all"
	if req.MailboxPath != nil {
		mailbox = *req.MailboxPath
	}
	s.log.Info("marked emails as read", "userId", p.UserID.String(), "count", marked, "mailbox", mailbox)
	writeJSON(w, http.StatusOK, map[string]any{"message": fmt.Sprintf("Marked %d emails as read.", marked), "markedCount": marked})
}
