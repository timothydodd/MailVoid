package api

import (
	"context"
	"database/sql"
	"errors"
)

// Entity structs mirror the C# models column-for-column. JSON tags use the camelCase
// names ASP.NET Core produced, and nullable columns are pointers so they serialise as null.

type User struct {
	ID           GUID    `json:"id"`
	UserName     string  `json:"userName"`
	PasswordHash string  `json:"-"`
	TimeStamp    DBTime  `json:"timeStamp"`
	Role         Role    `json:"role"`
	Subdomain    *string `json:"subdomain"`
}

const userCols = `Id, UserName, PasswordHash, TimeStamp, Role, Subdomain`

func scanUser(row interface{ Scan(...any) error }) (*User, error) {
	var u User
	if err := row.Scan(&u.ID, &u.UserName, &u.PasswordHash, &u.TimeStamp, &u.Role, &u.Subdomain); err != nil {
		return nil, err
	}
	return &u, nil
}

type Mail struct {
	ID            int64   `json:"id"`
	To            string  `json:"to"`
	Text          string  `json:"text"`
	IsHTML        bool    `json:"isHtml"`
	From          string  `json:"from"`
	FromName      *string `json:"fromName"`
	ToOthers      *string `json:"toOthers"`
	Subject       string  `json:"subject"`
	Charsets      *string `json:"charsets"`
	CreatedOn     DBTime  `json:"createdOn"`
	MailGroupPath *string `json:"mailGroupPath"`
	Attachments   *string `json:"attachments"`
	Headers       *string `json:"headers"`
	RawSource     *string `json:"rawSource"`
	MessageID     *string `json:"messageId"`
	SpfResult     *string `json:"spfResult"`
	DkimResult    *string `json:"dkimResult"`
}

const mailCols = `m.Id, m.[To], m.Text, m.IsHtml, m.[From], m.FromName, m.ToOthers, m.Subject, m.Charsets, m.CreatedOn,
	m.MailGroupPath, m.Attachments, m.Headers, m.RawSource, m.MessageId, m.SpfResult, m.DkimResult`

func scanMail(row interface{ Scan(...any) error }) (*Mail, error) {
	var m Mail
	err := row.Scan(&m.ID, &m.To, &m.Text, &m.IsHTML, &m.From, &m.FromName, &m.ToOthers, &m.Subject, &m.Charsets,
		&m.CreatedOn, &m.MailGroupPath, &m.Attachments, &m.Headers, &m.RawSource, &m.MessageID, &m.SpfResult, &m.DkimResult)
	if err != nil {
		return nil, err
	}
	return &m, nil
}

type MailGroup struct {
	ID               int64
	Path             *string
	Subdomain        *string
	Description      *string
	OwnerUserID      GUID
	IsPublic         bool
	IsUserPrivate    bool
	IsDefaultMailbox bool
	CreatedAt        DBTime
	LastActivity     NullTime
	RetentionDays    *int
}

const mailGroupCols = `Id, Path, Subdomain, Description, OwnerUserId, IsPublic, IsUserPrivate, IsDefaultMailbox, CreatedAt, LastActivity, RetentionDays`

func scanMailGroup(row interface{ Scan(...any) error }) (*MailGroup, error) {
	var g MailGroup
	err := row.Scan(&g.ID, &g.Path, &g.Subdomain, &g.Description, &g.OwnerUserID, &g.IsPublic, &g.IsUserPrivate,
		&g.IsDefaultMailbox, &g.CreatedAt, &g.LastActivity, &g.RetentionDays)
	if err != nil {
		return nil, err
	}
	return &g, nil
}

type RefreshToken struct {
	ID          int64
	Token       string
	UserID      GUID
	ExpiryDate  DBTime
	IsRevoked   bool
	CreatedDate DBTime
}

type WebhookBucket struct {
	ID            int64    `json:"id"`
	Name          string   `json:"name"`
	Description   *string  `json:"description"`
	OwnerUserID   GUID     `json:"-"`
	IsPublic      bool     `json:"isPublic"`
	CreatedAt     DBTime   `json:"createdAt"`
	LastActivity  NullTime `json:"lastActivity"`
	RetentionDays *int     `json:"retentionDays"`
}

const bucketCols = `Id, Name, Description, OwnerUserId, IsPublic, CreatedAt, LastActivity, RetentionDays`

func scanBucket(row interface{ Scan(...any) error }) (*WebhookBucket, error) {
	var b WebhookBucket
	err := row.Scan(&b.ID, &b.Name, &b.Description, &b.OwnerUserID, &b.IsPublic, &b.CreatedAt, &b.LastActivity, &b.RetentionDays)
	if err != nil {
		return nil, err
	}
	return &b, nil
}

type Webhook struct {
	ID          int64   `json:"id"`
	BucketName  string  `json:"bucketName"`
	HTTPMethod  string  `json:"httpMethod"`
	Path        string  `json:"path"`
	QueryString *string `json:"queryString"`
	Headers     string  `json:"headers"`
	Body        string  `json:"body"`
	ContentType *string `json:"contentType"`
	SourceIP    *string `json:"sourceIp"`
	CreatedOn   DBTime  `json:"createdOn"`
}

const webhookCols = `Id, BucketName, HttpMethod, Path, QueryString, Headers, Body, ContentType, SourceIp, CreatedOn`

func scanWebhook(row interface{ Scan(...any) error }) (*Webhook, error) {
	var w Webhook
	err := row.Scan(&w.ID, &w.BucketName, &w.HTTPMethod, &w.Path, &w.QueryString, &w.Headers, &w.Body, &w.ContentType, &w.SourceIP, &w.CreatedOn)
	if err != nil {
		return nil, err
	}
	return &w, nil
}

// queryOne runs a single-row query and maps sql.ErrNoRows to (nil, nil), like SingleAsync.
func queryOne[T any](ctx context.Context, db *sql.DB, scan func(interface{ Scan(...any) error }) (*T, error), query string, args ...any) (*T, error) {
	v, err := scan(db.QueryRowContext(ctx, query, args...))
	if errors.Is(err, sql.ErrNoRows) {
		return nil, nil
	}
	return v, err
}

func queryAll[T any](ctx context.Context, db *sql.DB, scan func(interface{ Scan(...any) error }) (*T, error), query string, args ...any) ([]*T, error) {
	rows, err := db.QueryContext(ctx, query, args...)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	var out []*T
	for rows.Next() {
		v, err := scan(rows)
		if err != nil {
			return nil, err
		}
		out = append(out, v)
	}
	return out, rows.Err()
}

func scalarInt(ctx context.Context, db *sql.DB, query string, args ...any) (int64, error) {
	var n int64
	err := db.QueryRowContext(ctx, query, args...).Scan(&n)
	return n, err
}

func strPtr(s string) *string { return &s }

func deref(s *string) string {
	if s == nil {
		return ""
	}
	return *s
}
