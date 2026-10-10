package api

import (
	"context"
	"time"
)

// Orphaned mail (its MailGroup was deleted) isn't covered by any per-mailbox retention
// rule, so it is reclaimed after this many days.
const globalOrphanMaxAgeDays = 30

// RunCleanup runs the mail and webhook retention sweeps immediately and then hourly,
// like MailCleanupService and WebhookCleanupService.
func (s *Server) RunCleanup(ctx context.Context) {
	ticker := time.NewTicker(time.Hour)
	defer ticker.Stop()
	for {
		s.cleanupMail(ctx)
		s.cleanupWebhooks(ctx)
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
		}
	}
}

func (s *Server) cleanupMail(ctx context.Context) {
	s.log.Info("starting mail cleanup cycle")
	groups, err := queryAll(ctx, s.db, scanMailGroup, `SELECT `+mailGroupCols+` FROM MailGroup WHERE RetentionDays IS NOT NULL AND RetentionDays > 0`)
	if err != nil {
		s.log.Error("mail cleanup failed", "error", err)
		return
	}
	for _, g := range groups {
		if ctx.Err() != nil {
			return
		}
		cutoff := DBTime{time.Now().UTC().AddDate(0, 0, -*g.RetentionDays)}
		// UserMailRead rows go with the declared ON DELETE CASCADE.
		res, err := s.db.ExecContext(ctx, `DELETE FROM Mail WHERE MailGroupPath = ? AND CreatedOn < ?`, g.Path, cutoff)
		if err != nil {
			s.log.Error("error cleaning up mailbox", "path", deref(g.Path), "days", *g.RetentionDays, "error", err)
			continue
		}
		if n, _ := res.RowsAffected(); n > 0 {
			s.log.Info("deleted expired emails", "count", n, "path", deref(g.Path), "days", *g.RetentionDays)
		}
	}

	cutoff := DBTime{time.Now().UTC().AddDate(0, 0, -globalOrphanMaxAgeDays)}
	res, err := s.db.ExecContext(ctx, `DELETE FROM Mail
		WHERE CreatedOn < ?
		  AND (MailGroupPath IS NULL
		       OR NOT EXISTS (SELECT 1 FROM MailGroup mg WHERE mg.Path = Mail.MailGroupPath))`, cutoff)
	if err != nil {
		s.log.Error("error cleaning up orphaned mail", "error", err)
	} else if n, _ := res.RowsAffected(); n > 0 {
		s.log.Info("deleted orphaned emails", "count", n, "days", globalOrphanMaxAgeDays)
	}
	s.log.Info("mail cleanup cycle completed")
}

func (s *Server) cleanupWebhooks(ctx context.Context) {
	buckets, err := queryAll(ctx, s.db, scanBucket, `SELECT `+bucketCols+` FROM WebhookBucket WHERE RetentionDays IS NOT NULL AND RetentionDays > 0`)
	if err != nil {
		s.log.Error("webhook cleanup failed", "error", err)
		return
	}
	for _, b := range buckets {
		if ctx.Err() != nil {
			return
		}
		cutoff := DBTime{time.Now().UTC().AddDate(0, 0, -*b.RetentionDays)}
		res, err := s.db.ExecContext(ctx, `DELETE FROM Webhook WHERE BucketName = ? AND CreatedOn < ?`, b.Name, cutoff)
		if err != nil {
			s.log.Error("error cleaning up webhook bucket", "name", b.Name, "error", err)
			continue
		}
		if n, _ := res.RowsAffected(); n > 0 {
			s.log.Info("deleted expired webhooks", "count", n, "bucket", b.Name, "days", *b.RetentionDays)
		}
	}
}
