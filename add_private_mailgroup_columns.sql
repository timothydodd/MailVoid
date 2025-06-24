-- Migration to add private MailGroup columns
-- Run this manually to add the new columns needed for private mailboxes

-- Add new columns to MailGroup table
ALTER TABLE MailGroup 
ADD COLUMN IsUserPrivate TINYINT(1) NOT NULL DEFAULT 0;

ALTER TABLE MailGroup 
ADD COLUMN UserEmailAddress VARCHAR(255) NULL;

-- Add index for UserEmailAddress for faster lookups
CREATE INDEX IX_MailGroup_UserEmailAddress ON MailGroup(UserEmailAddress);

-- Convert existing ClaimedMailboxes to private MailGroups
INSERT INTO MailGroup (Path, Subdomain, Description, OwnerUserId, IsPublic, IsUserPrivate, UserEmailAddress, CreatedAt)
SELECT 
    CONCAT('user-', u.UserName) as Path,
    SUBSTRING_INDEX(SUBSTRING_INDEX(cm.EmailAddress, '@', -1), '.', 1) as Subdomain,
    CONCAT('Private mailbox for ', u.UserName) as Description,
    cm.UserId as OwnerUserId,
    0 as IsPublic,
    1 as IsUserPrivate,
    cm.EmailAddress as UserEmailAddress,
    cm.ClaimedOn as CreatedAt
FROM ClaimedMailbox cm
INNER JOIN User u ON cm.UserId = u.Id
WHERE cm.IsActive = 1;

-- Update Mail records to use the new private group paths
UPDATE Mail m
INNER JOIN ClaimedMailbox cm ON m.To = cm.EmailAddress
INNER JOIN User u ON cm.UserId = u.Id
SET m.MailGroupPath = CONCAT('user-', u.UserName)
WHERE cm.IsActive = 1
AND (m.MailGroupPath IS NULL OR m.MailGroupPath = '' OR m.MailGroupPath LIKE 'user-%');

-- Verify the migration worked
SELECT 'ClaimedMailboxes converted:' as Status, COUNT(*) as Count
FROM MailGroup 
WHERE IsUserPrivate = 1;

SELECT 'Mail records updated:' as Status, COUNT(*) as Count  
FROM Mail m
INNER JOIN MailGroup mg ON m.MailGroupPath = mg.Path
WHERE mg.IsUserPrivate = 1;