-- Fix CreatedOn column default value for MySQL strict mode
-- This addresses the 'Invalid default value for CreatedOn' error

-- For Mail table
ALTER TABLE Mail MODIFY COLUMN CreatedOn DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;

-- For MailGroup table 
ALTER TABLE MailGroup MODIFY COLUMN CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;

-- For RefreshToken table
ALTER TABLE RefreshToken MODIFY COLUMN CreatedDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;

-- For MailGroupUser table (if it exists)
ALTER TABLE MailGroupUser MODIFY COLUMN GrantedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP;