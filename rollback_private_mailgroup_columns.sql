-- Rollback script for private MailGroup columns
-- Run this to undo the private mailbox changes

-- Remove private MailGroup records (this will not affect the original ClaimedMailbox data)
DELETE FROM MailGroup WHERE IsUserPrivate = 1;

-- Reset Mail records back to their original subdomain-based paths
-- (This is approximate - you may want to backup before running the forward migration)
UPDATE Mail m
SET m.MailGroupPath = CONCAT('subdomain/', SUBSTRING_INDEX(SUBSTRING_INDEX(m.To, '@', -1), '.', 1))
WHERE m.MailGroupPath LIKE 'user-%';

-- Remove the new columns
DROP INDEX IX_MailGroup_UserEmailAddress ON MailGroup;
ALTER TABLE MailGroup DROP COLUMN UserEmailAddress;
ALTER TABLE MailGroup DROP COLUMN IsUserPrivate;