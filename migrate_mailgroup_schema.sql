-- MailGroup Table Schema Migration Script
-- Migrates from rules-based to subdomain-based mail groups
-- Run this script against your existing database

-- Start transaction for safety
START TRANSACTION;

-- Create backup of existing MailGroup table
CREATE TABLE IF NOT EXISTS MailGroup_backup AS SELECT * FROM MailGroup;

-- Add new columns to MailGroup table (check if they exist first)
SET @sql = '';

-- Check and add Subdomain column
SELECT COUNT(*) INTO @col_exists 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_SCHEMA = DATABASE() 
  AND TABLE_NAME = 'MailGroup' 
  AND COLUMN_NAME = 'Subdomain';

SET @sql = IF(@col_exists = 0, 
    'ALTER TABLE MailGroup ADD COLUMN Subdomain VARCHAR(255) NULL AFTER Path', 
    'SELECT "Subdomain column already exists" as Info');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Check and add Description column
SELECT COUNT(*) INTO @col_exists 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_SCHEMA = DATABASE() 
  AND TABLE_NAME = 'MailGroup' 
  AND COLUMN_NAME = 'Description';

SET @sql = IF(@col_exists = 0, 
    'ALTER TABLE MailGroup ADD COLUMN Description TEXT NULL AFTER Subdomain', 
    'SELECT "Description column already exists" as Info');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Check and add CreatedAt column
SELECT COUNT(*) INTO @col_exists 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_SCHEMA = DATABASE() 
  AND TABLE_NAME = 'MailGroup' 
  AND COLUMN_NAME = 'CreatedAt';

SET @sql = IF(@col_exists = 0, 
    'ALTER TABLE MailGroup ADD COLUMN CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP AFTER IsPublic', 
    'SELECT "CreatedAt column already exists" as Info');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Create the new MailGroupUser table for user access management
CREATE TABLE IF NOT EXISTS MailGroupUser (
    Id BIGINT AUTO_INCREMENT PRIMARY KEY,
    MailGroupId BIGINT NOT NULL,
    UserId CHAR(36) NOT NULL,
    GrantedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UNIQUE INDEX IX_MailGroupUser_MailGroupId_UserId (MailGroupId, UserId),
    INDEX IX_MailGroupUser_MailGroupId (MailGroupId),
    INDEX IX_MailGroupUser_UserId (UserId),
    CONSTRAINT FK_MailGroupUser_MailGroup FOREIGN KEY (MailGroupId) 
        REFERENCES MailGroup(Id) ON DELETE CASCADE,
    CONSTRAINT FK_MailGroupUser_User FOREIGN KEY (UserId) 
        REFERENCES User(Id) ON DELETE CASCADE
);

-- Create EF Core migrations history table if it doesn't exist
CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (
    MigrationId VARCHAR(150) NOT NULL PRIMARY KEY,
    ProductVersion VARCHAR(32) NOT NULL
);

-- Function to extract subdomain from email or path
DELIMITER //
CREATE FUNCTION IF NOT EXISTS ExtractSubdomain(input_text VARCHAR(255)) 
RETURNS VARCHAR(255)
READS SQL DATA
DETERMINISTIC
BEGIN
    DECLARE domain_part VARCHAR(255);
    DECLARE subdomain VARCHAR(255);
    DECLARE dot_pos INT;
    
    -- If input contains @, treat as email and extract domain
    IF LOCATE('@', input_text) > 0 THEN
        SET domain_part = SUBSTRING_INDEX(input_text, '@', -1);
        SET dot_pos = LOCATE('.', domain_part);
        
        IF dot_pos > 0 THEN
            SET subdomain = SUBSTRING(domain_part, 1, dot_pos - 1);
        ELSE
            SET subdomain = domain_part;
        END IF;
    -- If input starts with 'subdomain/', extract the part after it
    ELSEIF input_text LIKE 'subdomain/%' THEN
        SET subdomain = SUBSTRING(input_text, 11); -- Length of 'subdomain/' + 1
    -- Otherwise, clean up the path to make it subdomain-like
    ELSE
        SET subdomain = REPLACE(REPLACE(REPLACE(input_text, '/', '_'), ' ', '_'), '-', '_');
    END IF;
    
    -- Return lowercase subdomain or 'default' if empty
    RETURN CASE 
        WHEN LENGTH(TRIM(subdomain)) > 0 THEN LOWER(TRIM(subdomain))
        ELSE 'default'
    END;
END//
DELIMITER ;

-- Update existing MailGroup records to populate new columns
-- Handle NULL or empty Path values first
UPDATE MailGroup 
SET Path = 'subdomain/default'
WHERE Id IN (
    SELECT temp_id FROM (
        SELECT Id as temp_id FROM MailGroup 
        WHERE Path IS NULL OR TRIM(Path) = ''
    ) as temp_table
);

-- Update existing MailGroup records to populate new columns
-- First, get all IDs that need updating to work with safe mode
UPDATE MailGroup 
SET 
    Subdomain = ExtractSubdomain(COALESCE(Path, 'subdomain/default')),
    Description = CASE 
        WHEN Rules IS NOT NULL AND Rules != '' THEN 
            CONCAT('Migrated from rules-based group: ', LEFT(COALESCE(Path, 'default'), 50))
        ELSE 
            CONCAT('Mail group for ', ExtractSubdomain(COALESCE(Path, 'subdomain/default')), ' subdomain')
    END,
    CreatedAt = COALESCE(CreatedAt, NOW())
WHERE Id IN (
    SELECT temp_id FROM (
        SELECT Id as temp_id FROM MailGroup WHERE Subdomain IS NULL
    ) as temp_table
);

-- Handle duplicate subdomains by appending numbers
-- Use a simpler approach with ROW_NUMBER() if supported, or manual numbering
UPDATE MailGroup mg1
JOIN (
    SELECT 
        Id,
        Subdomain,
        (SELECT COUNT(*) FROM MailGroup mg2 
         WHERE mg2.Subdomain = mg1.Subdomain AND mg2.Id <= mg1.Id
        ) as row_num
    FROM MailGroup mg1
    WHERE Subdomain IS NOT NULL
) numbered ON mg1.Id = numbered.Id
SET mg1.Subdomain = CASE 
    WHEN numbered.row_num > 1 THEN CONCAT(numbered.Subdomain, '_', numbered.row_num)
    ELSE numbered.Subdomain
END
WHERE numbered.row_num > 1;

-- Update Path column to match new subdomain format
UPDATE MailGroup 
SET Path = CONCAT('subdomain/', Subdomain)
WHERE Id IN (
    SELECT temp_id FROM (
        SELECT Id as temp_id FROM MailGroup 
        WHERE Path NOT LIKE CONCAT('subdomain/', Subdomain)
    ) as temp_table
);

-- Add indexes (non-unique since fields can be NULL)
CREATE INDEX IF NOT EXISTS IX_MailGroup_Path ON MailGroup(Path);
CREATE INDEX IF NOT EXISTS IX_MailGroup_Subdomain ON MailGroup(Subdomain);

-- Final cleanup: ensure no NULL values exist
-- First handle NULL OwnerUserId by setting to admin user
UPDATE MailGroup 
SET OwnerUserId = (SELECT Id FROM User WHERE UserName = 'admin' LIMIT 1)
WHERE Id IN (
    SELECT temp_id FROM (
        SELECT Id as temp_id FROM MailGroup 
        WHERE OwnerUserId IS NULL
    ) as temp_table
);

-- Then handle other NULL values
UPDATE MailGroup 
SET 
    Path = COALESCE(Path, 'subdomain/default'),
    Subdomain = COALESCE(Subdomain, 'default'),
    Description = COALESCE(Description, 'Default mail group')
WHERE Id IN (
    SELECT temp_id FROM (
        SELECT Id as temp_id FROM MailGroup 
        WHERE Path IS NULL OR Subdomain IS NULL
    ) as temp_table
);

-- Keep columns nullable to support unclaimed emails
-- Subdomain and Path can be NULL for unclaimed emails that don't get assigned to groups
SELECT 'Columns remain nullable to support unclaimed emails' as Info;

-- Drop the Rules column (we have a backup)
SELECT COUNT(*) INTO @col_exists 
FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_SCHEMA = DATABASE() 
  AND TABLE_NAME = 'MailGroup' 
  AND COLUMN_NAME = 'Rules';

SET @sql = IF(@col_exists > 0, 
    'ALTER TABLE MailGroup DROP COLUMN Rules', 
    'SELECT "Rules column does not exist" as Info');
PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- Clean up temporary function
DROP FUNCTION IF EXISTS ExtractSubdomain;

-- Show summary of changes
SELECT 
    'Migration Summary' as Category,
    COUNT(*) as Count,
    'Total MailGroups Updated' as Description
FROM MailGroup;

SELECT 
    'Schema Changes' as Category,
    'Columns Added: Subdomain, Description, CreatedAt' as Changes,
    'Columns Removed: Rules' as Removed,
    'New Table: MailGroupUser' as NewTables;

-- Verification queries
SELECT 'VERIFICATION - MailGroup Structure:' as Info;
DESCRIBE MailGroup;

SELECT 'VERIFICATION - Sample MailGroups:' as Info;
SELECT Id, Path, Subdomain, Description, IsPublic, CreatedAt 
FROM MailGroup 
LIMIT 5;

SELECT 'VERIFICATION - Check for NULL values:' as Info;
SELECT 
    COUNT(*) as TotalRecords,
    SUM(CASE WHEN Path IS NULL THEN 1 ELSE 0 END) as NullPathCount,
    SUM(CASE WHEN Subdomain IS NULL THEN 1 ELSE 0 END) as NullSubdomainCount,
    SUM(CASE WHEN OwnerUserId IS NULL THEN 1 ELSE 0 END) as NullOwnerCount
FROM MailGroup;

-- Show any records that still have NULL in required fields
SELECT 'Records with NULL required fields:' as Warning;
SELECT Id, Path, Subdomain, OwnerUserId 
FROM MailGroup 
WHERE Path IS NULL OR Subdomain IS NULL OR OwnerUserId IS NULL
LIMIT 5;

-- Commit the transaction
COMMIT;

-- Handle EF Core migration tracking
-- Mark the InitialCreate migration as applied to prevent conflicts
SET @migration_name = '20250624160008_InitialCreate';

-- Insert migration record if it doesn't exist
INSERT IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion)
SELECT @migration_name, '8.0.17'
WHERE NOT EXISTS (
    SELECT 1 FROM __EFMigrationsHistory 
    WHERE MigrationId = @migration_name
);

-- Show current migration history
SELECT 'EF Migration History:' as Info;
SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory ORDER BY MigrationId;

-- Final instructions
SELECT '=== MIGRATION COMPLETED SUCCESSFULLY ===' as Status;
SELECT 'Backup table created: MailGroup_backup' as BackupInfo;
SELECT 'MailGroupUser table created with proper relationships' as NewTable;
SELECT 'EF Migration marked as applied to prevent conflicts' as EFInfo;
SELECT 'You can now run: dotnet ef database update' as NextStep;
SELECT 'To rollback: DROP TABLE MailGroup; RENAME TABLE MailGroup_backup TO MailGroup;' as RollbackInfo;