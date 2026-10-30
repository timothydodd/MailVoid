package api

import (
	"context"
	"database/sql"
	"fmt"
	"net/url"
	"strconv"
	"strings"
	"time"

	_ "modernc.org/sqlite"
)

// OpenDatabase opens the SQLite database described by an ADO.NET-style connection string
// ("Data Source=mailvoid.db"), the same value the .NET API read from
// ConnectionStrings:DefaultConnection.
//
// journalMode is applied to file databases (Database:JournalMode). Empty means WAL, which
// lets readers proceed while mail is being written. Use "DELETE" when the file lives on a
// network filesystem (NFS/SMB), where WAL's shared-memory index is unsafe.
func OpenDatabase(connectionString, journalMode string) (*sql.DB, error) {
	opts := parseConnectionString(connectionString)

	path := firstValue(opts, "data source", "datasource", "filename")
	if path == "" {
		return nil, fmt.Errorf("connection string has no Data Source: %q", connectionString)
	}

	timeoutMs := 30000 // Microsoft.Data.Sqlite's default command timeout is 30s
	if t, err := strconv.Atoi(firstValue(opts, "default timeout", "defaulttimeout", "command timeout")); err == nil && t > 0 {
		timeoutMs = t * 1000
	}

	q := url.Values{}
	// Foreign keys must be enabled per connection; the schema relies on ON DELETE CASCADE.
	q.Add("_pragma", "foreign_keys(1)")
	q.Add("_pragma", fmt.Sprintf("busy_timeout(%d)", timeoutMs))
	q.Set("_txlock", "immediate")

	dsn := path
	switch strings.ToLower(opts["mode"]) {
	case "readonly":
		q.Set("mode", "ro")
		dsn = "file:" + path
	case "memory":
		q.Set("mode", "memory")
		dsn = "file:" + path
	default:
		mode := strings.ToUpper(strings.TrimSpace(journalMode))
		if mode == "" {
			mode = "WAL"
		}
		if !validJournalModes[mode] {
			return nil, fmt.Errorf("unsupported Database:JournalMode %q", journalMode)
		}
		q.Add("_pragma", "journal_mode("+mode+")")
		if mode == "WAL" {
			// NORMAL is durable against application crashes in WAL mode and avoids an
			// fsync on every commit.
			q.Add("_pragma", "synchronous(NORMAL)")
		}
	}
	db, err := sql.Open("sqlite", dsn+"?"+q.Encode())
	if err != nil {
		return nil, err
	}
	db.SetMaxOpenConns(8)
	// Keep the whole pool warm; the default of 2 idle connections means bursts of
	// requests keep reopening the file and re-applying the pragmas.
	db.SetMaxIdleConns(8)
	db.SetConnMaxIdleTime(5 * time.Minute)
	return db, db.Ping()
}

var validJournalModes = map[string]bool{"WAL": true, "DELETE": true, "TRUNCATE": true, "PERSIST": true}

func parseConnectionString(cs string) map[string]string {
	out := map[string]string{}
	for _, part := range strings.Split(cs, ";") {
		k, v, ok := strings.Cut(part, "=")
		if !ok {
			continue
		}
		v = strings.TrimSpace(v)
		v = strings.Trim(v, `"'`)
		out[strings.ToLower(strings.TrimSpace(k))] = v
	}
	return out
}

func firstValue(m map[string]string, keys ...string) string {
	for _, k := range keys {
		if v := m[k]; v != "" {
			return v
		}
	}
	return ""
}

// schema is the current table layout, identical to what RoboDodd.OrmLite created for the
// .NET API. Only creation of missing tables/indexes is supported; there are no migrations.
var schema = []string{
	`CREATE TABLE IF NOT EXISTS [User] (
    [Id] TEXT PRIMARY KEY,
    [UserName] VARCHAR(255) NOT NULL,
    [PasswordHash] VARCHAR(255) NOT NULL,
    [TimeStamp] TEXT NOT NULL,
    [Role] TEXT NOT NULL,
    [Subdomain] VARCHAR(255)
)`,
	`CREATE UNIQUE INDEX IF NOT EXISTS IX_User_UserName ON [User] ([UserName])`,
	`CREATE UNIQUE INDEX IF NOT EXISTS IX_User_Subdomain ON [User] ([Subdomain])`,

	`CREATE TABLE IF NOT EXISTS [Mail] (
    [Id] INTEGER PRIMARY KEY AUTOINCREMENT,
    [To] TEXT NOT NULL,
    [Text] TEXT NOT NULL,
    [IsHtml] INTEGER NOT NULL,
    [From] TEXT NOT NULL,
    [FromName] TEXT,
    [ToOthers] TEXT,
    [Subject] TEXT NOT NULL,
    [Charsets] TEXT,
    [CreatedOn] TEXT NOT NULL,
    [MailGroupPath] TEXT,
    [Attachments] TEXT,
    [Headers] TEXT,
    [RawSource] TEXT,
    [MessageId] TEXT,
    [SpfResult] TEXT,
    [DkimResult] TEXT
)`,
	`CREATE INDEX IF NOT EXISTS IX_Mail_To ON [Mail] ([To])`,
	`CREATE INDEX IF NOT EXISTS IX_Mail_From ON [Mail] ([From])`,
	`CREATE INDEX IF NOT EXISTS IX_Mail_MailGroupPath ON [Mail] ([MailGroupPath])`,
	// List ordering, per-mailbox retention sweeps and orphan cleanup all filter or sort
	// on CreatedOn.
	`CREATE INDEX IF NOT EXISTS IX_Mail_MailGroupPath_CreatedOn ON [Mail] ([MailGroupPath], [CreatedOn])`,
	`CREATE INDEX IF NOT EXISTS IX_Mail_CreatedOn ON [Mail] ([CreatedOn])`,

	`CREATE TABLE IF NOT EXISTS [Contact] (
    [Id] INTEGER PRIMARY KEY AUTOINCREMENT,
    [From] TEXT NOT NULL,
    [Name] TEXT NOT NULL
)`,
	`CREATE UNIQUE INDEX IF NOT EXISTS IX_Contact_From ON [Contact] ([From])`,

	`CREATE TABLE IF NOT EXISTS [MailGroup] (
    [Id] INTEGER PRIMARY KEY AUTOINCREMENT,
    [Path] TEXT,
    [Subdomain] TEXT,
    [Description] TEXT,
    [OwnerUserId] TEXT NOT NULL,
    [IsPublic] INTEGER NOT NULL,
    [IsUserPrivate] INTEGER NOT NULL,
    [IsDefaultMailbox] INTEGER NOT NULL,
    [CreatedAt] TEXT NOT NULL,
    [LastActivity] TEXT,
    [RetentionDays] INTEGER
,        CONSTRAINT FK_MailGroup_OwnerUserId FOREIGN KEY ([OwnerUserId]) REFERENCES [User](Id) ON DELETE CASCADE
)`,
	`CREATE INDEX IF NOT EXISTS IX_MailGroup_Path ON [MailGroup] ([Path])`,
	`CREATE INDEX IF NOT EXISTS IX_MailGroup_Subdomain ON [MailGroup] ([Subdomain])`,

	`CREATE TABLE IF NOT EXISTS [MailGroupUser] (
    [Id] INTEGER PRIMARY KEY AUTOINCREMENT,
    [MailGroupId] INTEGER NOT NULL,
    [UserId] TEXT NOT NULL,
    [GrantedAt] TEXT NOT NULL
,        CONSTRAINT FK_MailGroupUser_MailGroupId FOREIGN KEY ([MailGroupId]) REFERENCES [MailGroup](Id) ON DELETE CASCADE
,        CONSTRAINT FK_MailGroupUser_UserId FOREIGN KEY ([UserId]) REFERENCES [User](Id) ON DELETE CASCADE
)`,
	`CREATE UNIQUE INDEX IF NOT EXISTS MailGroupId_UserId_idx ON [MailGroupUser] ([MailGroupId], [UserId])`,

	`CREATE TABLE IF NOT EXISTS [RefreshToken] (
    [Id] INTEGER PRIMARY KEY AUTOINCREMENT,
    [Token] TEXT NOT NULL,
    [UserId] TEXT NOT NULL,
    [ExpiryDate] TEXT NOT NULL,
    [IsRevoked] INTEGER NOT NULL,
    [CreatedDate] TEXT NOT NULL
,        CONSTRAINT FK_RefreshToken_UserId FOREIGN KEY ([UserId]) REFERENCES [User](Id) ON DELETE CASCADE
)`,
	`CREATE INDEX IF NOT EXISTS IX_RefreshToken_Token ON [RefreshToken] ([Token])`,
	`CREATE INDEX IF NOT EXISTS Token_UserId_idx ON [RefreshToken] ([Token], [UserId])`,
	`CREATE INDEX IF NOT EXISTS IX_RefreshToken_UserId ON [RefreshToken] ([UserId])`,

	`CREATE TABLE IF NOT EXISTS [UserMailRead] (
    [Id] INTEGER PRIMARY KEY AUTOINCREMENT,
    [UserId] TEXT NOT NULL,
    [MailId] INTEGER NOT NULL,
    [ReadAt] TEXT NOT NULL
,        CONSTRAINT FK_UserMailRead_UserId FOREIGN KEY ([UserId]) REFERENCES [User](Id) ON DELETE CASCADE
,        CONSTRAINT FK_UserMailRead_MailId FOREIGN KEY ([MailId]) REFERENCES [Mail](Id) ON DELETE CASCADE
)`,
	`CREATE UNIQUE INDEX IF NOT EXISTS UserId_MailId_idx ON [UserMailRead] ([UserId], [MailId])`,
	// ON DELETE CASCADE from Mail looks rows up by MailId; without this every deleted mail
	// scans the whole table.
	`CREATE INDEX IF NOT EXISTS IX_UserMailRead_MailId ON [UserMailRead] ([MailId])`,

	`CREATE TABLE IF NOT EXISTS [HiddenMailGroup] (
    [Id] INTEGER PRIMARY KEY AUTOINCREMENT,
    [UserId] TEXT NOT NULL,
    [MailGroupId] INTEGER NOT NULL,
    [CreatedAt] TEXT NOT NULL
,        CONSTRAINT FK_HiddenMailGroup_UserId FOREIGN KEY ([UserId]) REFERENCES [User](Id) ON DELETE CASCADE
,        CONSTRAINT FK_HiddenMailGroup_MailGroupId FOREIGN KEY ([MailGroupId]) REFERENCES [MailGroup](Id) ON DELETE CASCADE
)`,
	`CREATE UNIQUE INDEX IF NOT EXISTS UserId_MailGroupId_idx ON [HiddenMailGroup] ([UserId], [MailGroupId])`,

	`CREATE TABLE IF NOT EXISTS [WebhookBucket] (
    [Id] INTEGER PRIMARY KEY AUTOINCREMENT,
    [Name] VARCHAR(255) NOT NULL,
    [Description] VARCHAR(500),
    [OwnerUserId] TEXT NOT NULL,
    [IsPublic] INTEGER NOT NULL,
    [CreatedAt] TEXT NOT NULL,
    [LastActivity] TEXT,
    [RetentionDays] INTEGER
)`,
	`CREATE UNIQUE INDEX IF NOT EXISTS Name_idx ON [WebhookBucket] ([Name])`,

	`CREATE TABLE IF NOT EXISTS [Webhook] (
    [Id] INTEGER PRIMARY KEY AUTOINCREMENT,
    [BucketName] VARCHAR(255) NOT NULL,
    [HttpMethod] VARCHAR(10) NOT NULL,
    [Path] VARCHAR(2048) NOT NULL,
    [QueryString] VARCHAR(4096),
    [Headers] TEXT NOT NULL,
    [Body] TEXT NOT NULL,
    [ContentType] VARCHAR(255),
    [SourceIp] VARCHAR(45),
    [CreatedOn] TEXT NOT NULL
)`,
	`CREATE INDEX IF NOT EXISTS BucketName_idx ON [Webhook] ([BucketName])`,
	`CREATE INDEX IF NOT EXISTS CreatedOn_idx ON [Webhook] ([CreatedOn])`,
	`CREATE INDEX IF NOT EXISTS IX_Webhook_BucketName_CreatedOn ON [Webhook] ([BucketName], [CreatedOn])`,

	// Personal API keys for the read-only /api/v1 endpoints. Only a SHA-256 hash of the
	// key is stored; Prefix is the first characters, shown in the UI to tell keys apart.
	`CREATE TABLE IF NOT EXISTS [UserApiKey] (
    [Id] INTEGER PRIMARY KEY AUTOINCREMENT,
    [UserId] TEXT NOT NULL,
    [Name] VARCHAR(100) NOT NULL,
    [KeyHash] TEXT NOT NULL,
    [Prefix] VARCHAR(16) NOT NULL,
    [CreatedAt] TEXT NOT NULL,
    [LastUsedAt] TEXT
,        CONSTRAINT FK_UserApiKey_UserId FOREIGN KEY ([UserId]) REFERENCES [User](Id) ON DELETE CASCADE
)`,
	`CREATE UNIQUE INDEX IF NOT EXISTS IX_UserApiKey_KeyHash ON [UserApiKey] ([KeyHash])`,
	`CREATE INDEX IF NOT EXISTS IX_UserApiKey_UserId ON [UserApiKey] ([UserId])`,

	// Non-admin users listed here may use the webhook capture UI/API. Admins always can.
	`CREATE TABLE IF NOT EXISTS [UserWebhookAccess] (
    [UserId] TEXT PRIMARY KEY,
    [GrantedAt] TEXT NOT NULL
,        CONSTRAINT FK_UserWebhookAccess_UserId FOREIGN KEY ([UserId]) REFERENCES [User](Id) ON DELETE CASCADE
)`,
}

func InitSchema(ctx context.Context, db *sql.DB) error {
	for _, stmt := range schema {
		if _, err := db.ExecContext(ctx, stmt); err != nil {
			return fmt.Errorf("schema: %w\n%s", err, stmt)
		}
	}
	return nil
}
