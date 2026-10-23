package api

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"net/http"
	"regexp"
	"sort"
	"strconv"
	"strings"
	"time"
)

// handleMailWebhook receives mail forwarded by the SMTP server (WebhookController).
func (s *Server) handleMailWebhook(w http.ResponseWriter, r *http.Request) {
	var data MailData
	// Decode from the stream: this body can be up to 128 MB, and reading it into a
	// buffer first would hold it in memory twice.
	if err := json.NewDecoder(r.Body).Decode(&data); err != nil {
		writeBodyError(w, err)
		return
	}
	s.log.Info("received MailData", "from", data.From, "to", data.To)

	ctx := r.Context()
	mail := extractMail(&data)
	if err := s.storeIncomingMail(ctx, mail); err != nil {
		s.log.Error("failed to process MailData", "from", data.From, "to", data.To, "error", err)
		writeText(w, http.StatusInternalServerError, "Failed to process email")
		return
	}
	s.log.Info("processed MailData", "from", mail.From, "to", mail.To, "id", mail.ID)

	payload := map[string]any{
		"id":            mail.ID,
		"from":          mail.From,
		"to":            mail.To,
		"subject":       mail.Subject,
		"receivedDate":  Now(),
		"mailGroupPath": mail.MailGroupPath,
	}
	// Notify only users who can see this mail's group (mirrors the mail-list visibility
	// rules) rather than broadcasting to every client.
	if mail.MailGroupPath != nil && *mail.MailGroupPath != "" {
		rows, err := s.db.QueryContext(ctx, `SELECT DISTINCT u.Id
			FROM [User] u
			LEFT JOIN MailGroup mg ON mg.Path = ?
			WHERE mg.Id IS NOT NULL AND (
				mg.OwnerUserId = u.Id
				OR mg.Subdomain = u.Subdomain
				OR EXISTS (SELECT 1 FROM MailGroupUser mgu WHERE mgu.MailGroupId = mg.Id AND mgu.UserId = u.Id)
				OR (u.Role = 1 AND mg.Subdomain = 'default')
			)`, *mail.MailGroupPath)
		if err != nil {
			s.log.Error("failed to resolve notification recipients", "error", err)
		} else {
			var ids []GUID
			for rows.Next() {
				var id GUID
				if rows.Scan(&id) == nil {
					ids = append(ids, id)
				}
			}
			rows.Close()
			for _, id := range ids {
				s.hub.SendToUser(id, "NewMail", payload)
			}
		}
	}
	w.WriteHeader(http.StatusOK)
}

func (s *Server) storeIncomingMail(ctx context.Context, m *Mail) error {
	if err := s.setMailPath(ctx, m); err != nil {
		return err
	}
	if m.MailGroupPath != nil && *m.MailGroupPath != "" {
		if _, err := s.db.ExecContext(ctx, `UPDATE MailGroup SET LastActivity = ? WHERE Id = (SELECT Id FROM MailGroup WHERE Path = ? LIMIT 1)`,
			NullTimeOf(time.Now()), *m.MailGroupPath); err != nil {
			return err
		}
	}
	res, err := s.db.ExecContext(ctx, `INSERT INTO Mail ([To], Text, IsHtml, [From], FromName, ToOthers, Subject, Charsets, CreatedOn,
			MailGroupPath, Attachments, Headers, RawSource, MessageId, SpfResult, DkimResult)
		VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
		m.To, m.Text, m.IsHTML, m.From, m.FromName, m.ToOthers, m.Subject, m.Charsets, m.CreatedOn,
		m.MailGroupPath, m.Attachments, m.Headers, m.RawSource, m.MessageID, m.SpfResult, m.DkimResult)
	if err != nil {
		return err
	}
	m.ID, err = res.LastInsertId()
	return err
}

// ---- Anonymous webhook capture (HooksController) ----

const maxWebhookBody = 5 * 1024 * 1024

func (s *Server) handleCaptureWebhook(w http.ResponseWriter, r *http.Request) {
	bucketName := r.PathValue("bucket")
	path := r.PathValue("path")
	s.log.Info("received webhook", "bucket", bucketName, "path", path, "method", r.Method)

	if r.ContentLength > maxWebhookBody {
		w.WriteHeader(http.StatusRequestEntityTooLarge)
		return
	}
	body, err := io.ReadAll(http.MaxBytesReader(w, r.Body, maxWebhookBody))
	if err != nil {
		var tooLarge *http.MaxBytesError
		if errors.As(err, &tooLarge) {
			w.WriteHeader(http.StatusRequestEntityTooLarge)
			return
		}
		writeText(w, http.StatusInternalServerError, "Failed to capture webhook")
		return
	}

	ctx := r.Context()
	bucket, err := s.getOrCreateBucket(ctx, bucketName)
	if errors.Is(err, errInvalidBucketName) {
		writeText(w, http.StatusBadRequest, "Bucket names may only contain letters, numbers, '.', '_' and '-' (max 64).")
		return
	}
	if errors.Is(err, errBucketLimit) {
		writeText(w, http.StatusForbidden, "Webhook bucket limit reached.")
		return
	}
	if err != nil {
		s.log.Error("failed to capture webhook", "bucket", bucketName, "error", err)
		writeText(w, http.StatusInternalServerError, "Failed to capture webhook")
		return
	}

	hook := &Webhook{
		BucketName: bucketName,
		HTTPMethod: r.Method,
		Path:       "/",
		Headers:    captureHeaders(r),
		Body:       string(body),
		CreatedOn:  Now(),
	}
	if path != "" {
		hook.Path = "/" + path
	}
	if r.URL.RawQuery != "" {
		hook.QueryString = strPtr("?" + r.URL.RawQuery)
	}
	if ct := r.Header.Get("Content-Type"); ct != "" {
		hook.ContentType = &ct
	}
	if ip := s.clientIP(r); ip != "" {
		hook.SourceIP = &ip
	}

	res, err := s.db.ExecContext(ctx, `INSERT INTO Webhook (BucketName, HttpMethod, Path, QueryString, Headers, Body, ContentType, SourceIp, CreatedOn)
		VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
		hook.BucketName, hook.HTTPMethod, hook.Path, hook.QueryString, hook.Headers, hook.Body, hook.ContentType, hook.SourceIP, hook.CreatedOn)
	if err == nil {
		hook.ID, err = res.LastInsertId()
	}
	if err == nil {
		_, err = s.db.ExecContext(ctx, `UPDATE WebhookBucket SET LastActivity = ? WHERE Id = ?`, NullTimeOf(time.Now()), bucket.ID)
	}
	if err == nil && s.maxWebhooksPerBucket > 0 {
		// Keep only the newest captures; Id order is insertion order.
		_, err = s.db.ExecContext(ctx, `DELETE FROM Webhook WHERE BucketName = ? AND Id <=
			(SELECT Id FROM Webhook WHERE BucketName = ? ORDER BY Id DESC LIMIT 1 OFFSET ?)`,
			bucketName, bucketName, s.maxWebhooksPerBucket)
	}
	if err != nil {
		s.log.Error("failed to capture webhook", "bucket", bucketName, "error", err)
		writeText(w, http.StatusInternalServerError, "Failed to capture webhook")
		return
	}
	s.log.Info("captured webhook", "bucket", bucketName, "id", hook.ID)

	// Only the bucket owner is notified, not every connected client.
	s.hub.SendToUser(bucket.OwnerUserID, "NewWebhook", map[string]any{
		"id":          hook.ID,
		"bucketName":  hook.BucketName,
		"httpMethod":  hook.HTTPMethod,
		"path":        hook.Path,
		"contentType": hook.ContentType,
		"createdOn":   hook.CreatedOn,
	})
	writeJSON(w, http.StatusOK, map[string]any{"message": "Webhook captured", "id": hook.ID})
}

// captureHeaders serialises request headers (minus X-Forwarded-*) as a JSON object, with
// repeated values comma-joined like StringValues.ToString().
func captureHeaders(r *http.Request) string {
	headers := map[string]string{}
	for k, v := range r.Header {
		if strings.HasPrefix(strings.ToLower(k), "x-forwarded") {
			continue
		}
		headers[k] = strings.Join(v, ",")
	}
	// net/http lifts these out of the header map; Kestrel kept them.
	if r.Host != "" {
		headers["Host"] = r.Host
	}
	if _, ok := headers["Content-Length"]; !ok && r.ContentLength > 0 {
		headers["Content-Length"] = strconv.FormatInt(r.ContentLength, 10)
	}
	b, _ := json.Marshal(headers)
	return string(b)
}

func (s *Server) bucketByName(ctx context.Context, name string) (*WebhookBucket, error) {
	return queryOne(ctx, s.db, scanBucket, `SELECT `+bucketCols+` FROM WebhookBucket WHERE Name = ? LIMIT 1`, name)
}

var (
	validBucketName      = regexp.MustCompile(`^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$`)
	errInvalidBucketName = errors.New("invalid bucket name")
	errBucketLimit       = errors.New("bucket limit reached")
)

func (s *Server) getOrCreateBucket(ctx context.Context, name string) (*WebhookBucket, error) {
	b, err := s.bucketByName(ctx, name)
	if err != nil || b != nil {
		return b, err
	}
	// Existing buckets keep working whatever their name; only new ones are checked.
	if !validBucketName.MatchString(name) {
		return nil, errInvalidBucketName
	}
	if s.maxWebhookBuckets > 0 {
		n, err := scalarInt(ctx, s.db, `SELECT COUNT(*) FROM WebhookBucket`)
		if err != nil {
			return nil, err
		}
		if n >= int64(s.maxWebhookBuckets) {
			s.log.Warn("webhook bucket limit reached; not creating bucket", "name", name, "limit", s.maxWebhookBuckets)
			return nil, errBucketLimit
		}
	}
	admin, err := s.userByName(ctx, "admin")
	if err != nil {
		return nil, err
	}
	if admin == nil {
		return nil, errors.New("admin user not found")
	}
	_, err = s.db.ExecContext(ctx, `INSERT INTO WebhookBucket (Name, Description, OwnerUserId, IsPublic, CreatedAt, LastActivity, RetentionDays)
		VALUES (?, ?, ?, 1, ?, NULL, 3)`, name, "Auto-created bucket for "+name, admin.ID, Now())
	if err != nil {
		// Lost a race with a concurrent insert; re-fetch the winner.
		if raced, ferr := s.bucketByName(ctx, name); ferr == nil && raced != nil {
			return raced, nil
		}
		return nil, err
	}
	s.log.Info("created webhook bucket", "name", name)
	return s.bucketByName(ctx, name)
}

// ---- Webhook management (WebhookManagementController) ----

func (s *Server) canAccessBucket(p *Principal, b *WebhookBucket) bool {
	return p.IsAdmin() || b.OwnerUserID.Equal(p.UserID) || b.IsPublic
}

func (s *Server) handleGetBuckets(w http.ResponseWriter, r *http.Request, p *Principal) {
	buckets, err := queryAll(r.Context(), s.db, scanBucket, `SELECT `+bucketCols+` FROM WebhookBucket`)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	result := []*WebhookBucket{}
	for _, b := range buckets {
		if s.canAccessBucket(p, b) {
			result = append(result, b)
		}
	}
	activity := func(b *WebhookBucket) time.Time {
		if b.LastActivity.Valid {
			return b.LastActivity.Time
		}
		return b.CreatedAt.Time
	}
	sort.SliceStable(result, func(i, j int) bool { return activity(result[i]).After(activity(result[j])) })
	writeJSON(w, http.StatusOK, result)
}

func (s *Server) handleGetBucket(w http.ResponseWriter, r *http.Request, p *Principal) {
	b, err := s.bucketByName(r.Context(), r.PathValue("name"))
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if b == nil {
		writeProblem(w, http.StatusNotFound)
		return
	}
	if !s.canAccessBucket(p, b) {
		forbid(w)
		return
	}
	writeJSON(w, http.StatusOK, b)
}

func (s *Server) handleGetWebhooks(w http.ResponseWriter, r *http.Request, p *Principal) {
	q := r.URL.Query()
	page, pageSize := 1, 50
	if v := q.Get("page"); v != "" {
		n, err := strconv.Atoi(v)
		if err != nil {
			writeProblem(w, http.StatusBadRequest)
			return
		}
		page = n
	}
	if v := q.Get("pageSize"); v != "" {
		n, err := strconv.Atoi(v)
		if err != nil {
			writeProblem(w, http.StatusBadRequest)
			return
		}
		pageSize = n
	}
	page = max(page, 1)
	pageSize = max(1, min(pageSize, 200))

	ctx := r.Context()
	name := r.PathValue("name")
	b, err := s.bucketByName(ctx, name)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if b == nil {
		writeText(w, http.StatusNotFound, "Bucket not found")
		return
	}
	if !s.canAccessBucket(p, b) {
		forbid(w)
		return
	}
	total, err := scalarInt(ctx, s.db, `SELECT COUNT(*) FROM Webhook WHERE BucketName = ?`, name)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	// Bodies are trimmed in the list (each can be up to 5 MB, so a full page could approach
	// 1 GB); GET /api/webhooks/{id} returns the complete body.
	hooks, err := queryAll(ctx, s.db, scanWebhook, `SELECT Id, BucketName, HttpMethod, Path, QueryString, Headers,
			substr(Body, 1, ?), ContentType, SourceIp, CreatedOn
		FROM Webhook WHERE BucketName = ? ORDER BY CreatedOn DESC LIMIT ? OFFSET ?`,
		webhookListBodyChars, name, pageSize, (page-1)*pageSize)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	items := make([]Webhook, 0, len(hooks))
	for _, h := range hooks {
		items = append(items, *h)
	}
	writeJSON(w, http.StatusOK, pagedResults[Webhook]{Items: items, TotalCount: total})
}

const webhookListBodyChars = 4096

// webhookWithAccess loads a webhook and its bucket; inaccessible webhooks are reported as
// 404 so their existence isn't disclosed.
func (s *Server) webhookWithAccess(w http.ResponseWriter, r *http.Request, p *Principal) *Webhook {
	id, ok := pathInt64(w, r, "id")
	if !ok {
		return nil
	}
	ctx := r.Context()
	hook, err := queryOne(ctx, s.db, scanWebhook, `SELECT `+webhookCols+` FROM Webhook WHERE Id = ?`, id)
	if err != nil {
		s.internalError(w, r, err)
		return nil
	}
	if hook == nil {
		writeProblem(w, http.StatusNotFound)
		return nil
	}
	b, err := s.bucketByName(ctx, hook.BucketName)
	if err != nil {
		s.internalError(w, r, err)
		return nil
	}
	if b == nil || !s.canAccessBucket(p, b) {
		writeProblem(w, http.StatusNotFound)
		return nil
	}
	return hook
}

func (s *Server) handleGetWebhook(w http.ResponseWriter, r *http.Request, p *Principal) {
	if hook := s.webhookWithAccess(w, r, p); hook != nil {
		writeJSON(w, http.StatusOK, hook)
	}
}

func (s *Server) handleDeleteWebhook(w http.ResponseWriter, r *http.Request, p *Principal) {
	hook := s.webhookWithAccess(w, r, p)
	if hook == nil {
		return
	}
	if _, err := s.db.ExecContext(r.Context(), `DELETE FROM Webhook WHERE Id = ?`, hook.ID); err != nil {
		s.internalError(w, r, err)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

func (s *Server) handleDeleteBucket(w http.ResponseWriter, r *http.Request, p *Principal) {
	ctx := r.Context()
	name := r.PathValue("name")
	b, err := s.bucketByName(ctx, name)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if b == nil {
		writeProblem(w, http.StatusNotFound)
		return
	}
	if !s.canAccessBucket(p, b) {
		forbid(w)
		return
	}
	if _, err := s.db.ExecContext(ctx, `DELETE FROM Webhook WHERE BucketName = ?`, name); err != nil {
		s.internalError(w, r, err)
		return
	}
	if _, err := s.db.ExecContext(ctx, `DELETE FROM WebhookBucket WHERE Id = ?`, b.ID); err != nil {
		s.internalError(w, r, err)
		return
	}
	w.WriteHeader(http.StatusNoContent)
}

// handlePurgeBucket deletes every capture in a bucket but keeps the bucket itself.
func (s *Server) handlePurgeBucket(w http.ResponseWriter, r *http.Request, p *Principal) {
	ctx := r.Context()
	name := r.PathValue("name")
	b, err := s.bucketByName(ctx, name)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	if b == nil {
		writeProblem(w, http.StatusNotFound)
		return
	}
	if !s.canAccessBucket(p, b) {
		forbid(w)
		return
	}
	res, err := s.db.ExecContext(ctx, `DELETE FROM Webhook WHERE BucketName = ?`, name)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	n, _ := res.RowsAffected()
	s.log.Info("purged webhook bucket", "bucket", name, "deleted", n)
	writeJSON(w, http.StatusOK, map[string]any{"deleted": n})
}

// handlePurgeAll deletes the captures of every bucket the caller can access; the
// buckets themselves are kept.
func (s *Server) handlePurgeAll(w http.ResponseWriter, r *http.Request, p *Principal) {
	ctx := r.Context()
	buckets, err := queryAll(ctx, s.db, scanBucket, `SELECT `+bucketCols+` FROM WebhookBucket`)
	if err != nil {
		s.internalError(w, r, err)
		return
	}
	var deleted int64
	for _, b := range buckets {
		if !s.canAccessBucket(p, b) {
			continue
		}
		res, err := s.db.ExecContext(ctx, `DELETE FROM Webhook WHERE BucketName = ?`, b.Name)
		if err != nil {
			s.internalError(w, r, err)
			return
		}
		n, _ := res.RowsAffected()
		deleted += n
	}
	s.log.Info("purged webhook history", "deleted", deleted)
	writeJSON(w, http.StatusOK, map[string]any{"deleted": deleted})
}
