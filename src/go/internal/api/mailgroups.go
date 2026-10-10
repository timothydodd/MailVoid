package api

import (
	"context"
	"errors"
	"fmt"
	"regexp"
	"strings"
)

var emailSubdomainRegex = regexp.MustCompile(`^[^@]+@([^@.]+)\.(.+)$`)

// extractSubdomain returns the first label of the recipient's domain ("x" for a@x.example.com).
func extractSubdomain(email string) string {
	if strings.TrimSpace(email) == "" {
		return "default"
	}
	if m := emailSubdomainRegex.FindStringSubmatch(strings.ToLower(strings.TrimSpace(email))); m != nil {
		return m[1]
	}
	if at := strings.Index(email, "@"); at > 0 && at < len(email)-1 {
		domain := email[at+1:]
		if dot := strings.Index(domain, "."); dot > 0 {
			return strings.ToLower(domain[:dot])
		}
	}
	return "default"
}

func mailGroupPath(subdomain string) string { return "subdomain/" + subdomain }

func userPrivatePath(username string) string { return "user-" + username }

func (s *Server) mailGroupByID(ctx context.Context, id int64) (*MailGroup, error) {
	return queryOne(ctx, s.db, scanMailGroup, `SELECT `+mailGroupCols+` FROM MailGroup WHERE Id = ?`, id)
}

// setMailPath routes mail to a user's private mailbox when the subdomain matches one,
// otherwise to "subdomain/{x}", creating that group on first use.
func (s *Server) setMailPath(ctx context.Context, m *Mail) error {
	subdomain := extractSubdomain(m.To)
	private, err := queryOne(ctx, s.db, scanMailGroup,
		`SELECT `+mailGroupCols+` FROM MailGroup WHERE IsUserPrivate = 1 AND Subdomain = ? LIMIT 1`, subdomain)
	if err != nil {
		return err
	}
	if private != nil {
		m.MailGroupPath = private.Path
		return nil
	}
	m.MailGroupPath = strPtr(mailGroupPath(subdomain))
	_, err = s.getOrCreateMailGroup(ctx, subdomain)
	return err
}

func (s *Server) getOrCreateMailGroup(ctx context.Context, subdomain string) (*MailGroup, error) {
	existing, err := queryOne(ctx, s.db, scanMailGroup, `SELECT `+mailGroupCols+` FROM MailGroup WHERE Subdomain = ? LIMIT 1`, subdomain)
	if err != nil || existing != nil {
		return existing, err
	}
	admin, err := s.userByName(ctx, "admin")
	if err != nil {
		return nil, err
	}
	if admin == nil {
		return nil, errors.New("admin user not found")
	}
	g := &MailGroup{
		Path:          strPtr(mailGroupPath(subdomain)),
		Subdomain:     strPtr(subdomain),
		Description:   strPtr(fmt.Sprintf("Auto-generated group for %s subdomain", subdomain)),
		OwnerUserID:   admin.ID,
		CreatedAt:     Now(),
		RetentionDays: intPtr(3),
	}
	if err := s.insertMailGroup(ctx, g); err != nil {
		// Lost a race with a concurrent insert; re-fetch the winner.
		raced, ferr := queryOne(ctx, s.db, scanMailGroup, `SELECT `+mailGroupCols+` FROM MailGroup WHERE Subdomain = ? LIMIT 1`, subdomain)
		if ferr == nil && raced != nil {
			return raced, nil
		}
		s.log.Error("failed to create mail group", "subdomain", subdomain, "error", err)
		return nil, err
	}
	s.log.Info("created new mail group", "subdomain", subdomain)
	return g, nil
}

func (s *Server) insertMailGroup(ctx context.Context, g *MailGroup) error {
	res, err := s.db.ExecContext(ctx,
		`INSERT INTO MailGroup (Path, Subdomain, Description, OwnerUserId, IsPublic, IsUserPrivate, IsDefaultMailbox, CreatedAt, LastActivity, RetentionDays)
		 VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
		g.Path, g.Subdomain, g.Description, g.OwnerUserID, g.IsPublic, g.IsUserPrivate, g.IsDefaultMailbox, g.CreatedAt, g.LastActivity, g.RetentionDays)
	if err != nil {
		return err
	}
	g.ID, _ = res.LastInsertId()
	return nil
}

// hasUserAccess: owner, public group, or explicit MailGroupUser grant.
func (s *Server) hasUserAccess(ctx context.Context, mailGroupID int64, userID GUID) (bool, error) {
	g, err := s.mailGroupByID(ctx, mailGroupID)
	if err != nil || g == nil {
		return false, err
	}
	if g.OwnerUserID.Equal(userID) || g.IsPublic {
		return true, nil
	}
	return s.hasExplicitAccess(ctx, mailGroupID, userID)
}

func (s *Server) hasExplicitAccess(ctx context.Context, mailGroupID int64, userID GUID) (bool, error) {
	n, err := scalarInt(ctx, s.db, `SELECT COUNT(*) FROM MailGroupUser WHERE MailGroupId = ? AND UserId = ?`, mailGroupID, userID)
	return n > 0, err
}

func (s *Server) createUserPrivateMailGroup(ctx context.Context, userID GUID, isDefault bool) error {
	u, err := s.userByID(ctx, userID)
	if err != nil {
		return err
	}
	if u == nil {
		return fmt.Errorf("user %s not found", userID)
	}
	g := &MailGroup{
		Path:             strPtr(userPrivatePath(u.UserName)),
		Subdomain:        strPtr(u.UserName),
		Description:      strPtr("Private mailbox for " + u.UserName),
		OwnerUserID:      userID,
		IsUserPrivate:    true,
		IsDefaultMailbox: isDefault,
		CreatedAt:        Now(),
		RetentionDays:    intPtr(3),
	}
	if err := s.insertMailGroup(ctx, g); err != nil {
		return err
	}
	s.log.Info("created private mail group", "userId", userID.String(), "subdomain", u.UserName)
	return nil
}

func intPtr(n int) *int { return &n }

// SeedDefaultData ensures the admin user (admin/admin) exists with the Admin role and a
// private mailbox, as DatabaseInitializer did.
func (s *Server) SeedDefaultData(ctx context.Context) error {
	admin, err := s.userByName(ctx, "admin")
	if err != nil {
		return err
	}
	if admin == nil {
		id := NewGUID()
		_, err := s.db.ExecContext(ctx,
			`INSERT INTO [User] (Id, UserName, PasswordHash, TimeStamp, Role, Subdomain) VALUES (?, 'admin', ?, ?, ?, NULL)`,
			id, hashPassword("admin"), Now(), RoleAdmin)
		if err != nil {
			return err
		}
		if err := s.createUserPrivateMailGroup(ctx, id, true); err != nil {
			return err
		}
	} else if admin.Role != RoleAdmin {
		if _, err := s.db.ExecContext(ctx, `UPDATE [User] SET Role = ? WHERE Id = ?`, RoleAdmin, admin.ID); err != nil {
			return err
		}
	}

	admin, err = s.userByName(ctx, "admin")
	if err != nil || admin == nil {
		return err
	}
	n, err := scalarInt(ctx, s.db, `SELECT COUNT(*) FROM MailGroup WHERE OwnerUserId = ? AND IsUserPrivate = 1`, admin.ID)
	if err != nil {
		return err
	}
	if n == 0 {
		return s.createUserPrivateMailGroup(ctx, admin.ID, true)
	}
	return nil
}
