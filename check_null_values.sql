-- Script to check for NULL values in MailGroup table
-- Run this to identify which columns have NULL values

SELECT 'Checking MailGroup table for NULL values...' as Status;

-- Check each column for NULL values
SELECT 
    'Path' as ColumnName,
    COUNT(*) as TotalRows,
    SUM(CASE WHEN Path IS NULL THEN 1 ELSE 0 END) as NullCount,
    SUM(CASE WHEN TRIM(Path) = '' THEN 1 ELSE 0 END) as EmptyCount
FROM MailGroup
UNION ALL
SELECT 
    'Subdomain' as ColumnName,
    COUNT(*) as TotalRows,
    SUM(CASE WHEN Subdomain IS NULL THEN 1 ELSE 0 END) as NullCount,
    SUM(CASE WHEN TRIM(Subdomain) = '' THEN 1 ELSE 0 END) as EmptyCount
FROM MailGroup
UNION ALL
SELECT 
    'Description' as ColumnName,
    COUNT(*) as TotalRows,
    SUM(CASE WHEN Description IS NULL THEN 1 ELSE 0 END) as NullCount,
    SUM(CASE WHEN TRIM(Description) = '' THEN 1 ELSE 0 END) as EmptyCount
FROM MailGroup
UNION ALL
SELECT 
    'OwnerUserId' as ColumnName,
    COUNT(*) as TotalRows,
    SUM(CASE WHEN OwnerUserId IS NULL THEN 1 ELSE 0 END) as NullCount,
    0 as EmptyCount
FROM MailGroup;

-- Show sample records with NULL values
SELECT 'Sample records with NULL values:' as Info;
SELECT Id, Path, Subdomain, Description, OwnerUserId, IsPublic, CreatedAt
FROM MailGroup 
WHERE Path IS NULL OR Subdomain IS NULL OR OwnerUserId IS NULL
LIMIT 10;

-- Show table structure
SELECT 'Current table structure:' as Info;
DESCRIBE MailGroup;