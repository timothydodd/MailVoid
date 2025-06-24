-- Script to mark EF Core migration as applied for existing database
-- Run this AFTER your schema migration script and BEFORE dotnet ef database update

-- Create the EF migrations history table if it doesn't exist
CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (
    MigrationId NVARCHAR(150) NOT NULL,
    ProductVersion NVARCHAR(32) NOT NULL,
    PRIMARY KEY (MigrationId)
);

-- Check if our InitialCreate migration is already recorded
SELECT COUNT(*) as MigrationExists 
FROM __EFMigrationsHistory 
WHERE MigrationId LIKE '%InitialCreate%';

-- Add the InitialCreate migration to history (replace the timestamp with your actual migration name)
-- You can find the exact name in your Migrations folder
INSERT IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion)
SELECT 
    (SELECT CONCAT(
        LEFT(REPLACE(REPLACE(NOW(6), '-', ''), ':', ''), 14),
        '_InitialCreate'
    )) as MigrationId,
    '8.0.17' as ProductVersion
WHERE NOT EXISTS (
    SELECT 1 FROM __EFMigrationsHistory 
    WHERE MigrationId LIKE '%InitialCreate%'
);

-- Show current migration history
SELECT * FROM __EFMigrationsHistory ORDER BY MigrationId;