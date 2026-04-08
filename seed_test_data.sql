-- MailVoid Test Data Seed Script
-- This script creates sample data for testing the MailVoid application
-- Run this after the application has started and created tables
-- The admin user (admin/admin) is already created by the application startup

-- Note: Password hashes are for "password123" - use this to login to test accounts

-- Purge all tables (keep only admin user)
DELETE FROM UserMailRead;
DELETE FROM MailGroupUser;
DELETE FROM RefreshToken;
DELETE FROM Webhook;
DELETE FROM WebhookBucket;
DELETE FROM Mail;
DELETE FROM Contact;
DELETE FROM MailGroup;
DELETE FROM `User` WHERE UserName != 'admin';

-- ===========================
-- USERS
-- ===========================

-- All test users use the same password as admin (admin/admin)

-- john - Regular user with subdomain "john"
INSERT INTO `User` (Id, UserName, PasswordHash, TimeStamp, Role, Subdomain)
SELECT '11111111-1111-1111-1111-111111111111', 'john', PasswordHash, NOW(), 0, 'john'
FROM `User` WHERE UserName = 'admin';

-- jane - Regular user with subdomain "jane"
INSERT INTO `User` (Id, UserName, PasswordHash, TimeStamp, Role, Subdomain)
SELECT '22222222-2222-2222-2222-222222222222', 'jane', PasswordHash, NOW(), 0, 'jane'
FROM `User` WHERE UserName = 'admin';

-- manager - Admin user with subdomain "manager"
INSERT INTO `User` (Id, UserName, PasswordHash, TimeStamp, Role, Subdomain)
SELECT '33333333-3333-3333-3333-333333333333', 'manager', PasswordHash, NOW(), 1, 'manager'
FROM `User` WHERE UserName = 'admin';

-- alice - Regular user with subdomain "alice"
INSERT INTO `User` (Id, UserName, PasswordHash, TimeStamp, Role, Subdomain)
SELECT '44444444-4444-4444-4444-444444444444', 'alice', PasswordHash, NOW(), 0, 'alice'
FROM `User` WHERE UserName = 'admin';

-- ===========================
-- MAIL GROUPS
-- ===========================

-- Default private mailboxes (auto-created per user, cannot be deleted)
INSERT INTO MailGroup (Id, Path, Subdomain, Description, OwnerUserId, IsPublic, IsUserPrivate, IsDefaultMailbox, CreatedAt, RetentionDays) VALUES
(100, 'user-john', 'john', 'Private mailbox for john', '11111111-1111-1111-1111-111111111111', 0, 1, 1, NOW(), 3),
(101, 'user-jane', 'jane', 'Private mailbox for jane', '22222222-2222-2222-2222-222222222222', 0, 1, 1, NOW(), 7),
(102, 'user-manager', 'manager', 'Private mailbox for manager', '33333333-3333-3333-3333-333333333333', 0, 1, 1, NOW(), 3),
(103, 'user-alice', 'alice', 'Private mailbox for alice', '44444444-4444-4444-4444-444444444444', 0, 1, 1, NOW(), 3);

-- Base domain group (emails to user@mailvoid.com with no subdomain)
-- Admins auto-see this via the 'default' subdomain rule
INSERT INTO MailGroup (Id, Path, Subdomain, Description, OwnerUserId, IsPublic, IsUserPrivate, IsDefaultMailbox, CreatedAt, RetentionDays) VALUES
(199, 'subdomain/default', 'default', 'Base domain emails', (SELECT Id FROM `User` WHERE UserName = 'admin'), 0, 0, 0, NOW(), 7);

-- Shared mail groups (owned by specific users, shared via MailGroupUser)
INSERT INTO MailGroup (Id, Path, Subdomain, Description, OwnerUserId, IsPublic, IsUserPrivate, IsDefaultMailbox, CreatedAt, RetentionDays) VALUES
(200, 'subdomain/support', 'support', 'Customer support emails', '33333333-3333-3333-3333-333333333333', 0, 0, 0, NOW(), 14),
(201, 'subdomain/sales', 'sales', 'Sales inquiries', '33333333-3333-3333-3333-333333333333', 0, 0, 0, NOW(), 30),
(202, 'subdomain/marketing', 'marketing', 'Marketing campaigns', '22222222-2222-2222-2222-222222222222', 0, 0, 0, NOW(), 7),
(203, 'subdomain/dev', 'dev', 'Development notifications', '11111111-1111-1111-1111-111111111111', 0, 0, 0, NOW(), 3),
(204, 'subdomain/staging', 'staging', 'Staging environment emails', '11111111-1111-1111-1111-111111111111', 0, 0, 0, NOW(), 1);

-- ===========================
-- MAIL GROUP SHARING (MailGroupUser)
-- ===========================

-- jane has access to support (owned by manager)
INSERT INTO MailGroupUser (MailGroupId, UserId, GrantedAt) VALUES
(200, '22222222-2222-2222-2222-222222222222', NOW());

-- john has access to support (shared by jane or manager)
INSERT INTO MailGroupUser (MailGroupId, UserId, GrantedAt) VALUES
(200, '11111111-1111-1111-1111-111111111111', NOW());

-- alice has access to marketing (owned by jane)
INSERT INTO MailGroupUser (MailGroupId, UserId, GrantedAt) VALUES
(202, '44444444-4444-4444-4444-444444444444', NOW());

-- manager shared dev with themselves (admin opting in)
INSERT INTO MailGroupUser (MailGroupId, UserId, GrantedAt) VALUES
(203, '33333333-3333-3333-3333-333333333333', NOW());

-- jane has access to staging (shared by john)
INSERT INTO MailGroupUser (MailGroupId, UserId, GrantedAt) VALUES
(204, '22222222-2222-2222-2222-222222222222', NOW());

-- ===========================
-- CONTACTS
-- ===========================

INSERT INTO Contact (Id, `From`, Name) VALUES
(1, 'noreply@company.com', 'Company Notifications'),
(2, 'support@helpdesk.com', 'Help Desk'),
(3, 'ci@buildserver.com', 'Build Server'),
(4, 'deploy@automation.com', 'Deployment Bot'),
(5, 'orders@ecommerce.com', 'Order Processing'),
(6, 'security@alerts.com', 'Security Alerts'),
(7, 'postmaster@mailvoid.com', 'Postmaster');

-- ===========================
-- EMAILS
-- ===========================

-- Base domain emails (visible to admins)
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(1, 'postmaster@mailvoid.com', 'Mail delivery report for the past 24 hours.', 0, 'postmaster@mailvoid.com', 'Postmaster', NULL, 'Daily Mail Delivery Report', 'utf-8', DATE_SUB(NOW(), INTERVAL 2 HOUR), 'subdomain/default'),
(2, 'abuse@mailvoid.com', 'Abuse report from external system.', 0, 'abuse@external.com', 'Abuse Reporter', NULL, 'Abuse Report #4821', 'utf-8', DATE_SUB(NOW(), INTERVAL 6 HOUR), 'subdomain/default'),
(3, 'info@mailvoid.com', 'Hi, I would like to learn more about your service.', 0, 'curious@example.com', 'Curious User', NULL, 'Service Inquiry', 'utf-8', DATE_SUB(NOW(), INTERVAL 1 DAY), 'subdomain/default');

-- John's private mailbox
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(10, 'welcome@john.mailvoid.com', '<h2>Welcome!</h2><p>Your mailbox is ready.</p>', 1, 'noreply@company.com', 'Company Notifications', NULL, 'Welcome to MailVoid', 'utf-8', DATE_SUB(NOW(), INTERVAL 5 DAY), 'user-john'),
(11, 'alerts@john.mailvoid.com', 'Your CI pipeline has been configured.', 0, 'ci@buildserver.com', 'Build Server', NULL, 'Pipeline Setup Complete', 'utf-8', DATE_SUB(NOW(), INTERVAL 2 DAY), 'user-john');

-- Jane's private mailbox
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(20, 'updates@jane.mailvoid.com', 'Your weekly summary is ready.', 0, 'noreply@company.com', 'Company Notifications', NULL, 'Weekly Summary', 'utf-8', DATE_SUB(NOW(), INTERVAL 1 DAY), 'user-jane'),
(21, 'notifications@jane.mailvoid.com', 'New comment on your pull request.', 0, 'noreply@github.com', 'GitHub', NULL, 'PR Comment: Fix login flow', 'utf-8', DATE_SUB(NOW(), INTERVAL 3 HOUR), 'user-jane');

-- Alice's private mailbox
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(30, 'test@alice.mailvoid.com', 'Test email for integration testing.', 0, 'ci@buildserver.com', 'Build Server', NULL, 'Integration Test Email', 'utf-8', DATE_SUB(NOW(), INTERVAL 4 HOUR), 'user-alice');

-- Support group emails (manager owns, john & jane shared)
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(40, 'help@support.mailvoid.com', 'I cannot log in to my account.', 0, 'customer1@example.com', 'John Customer', NULL, 'Login Issue', 'utf-8', DATE_SUB(NOW(), INTERVAL 8 HOUR), 'subdomain/support'),
(41, 'ticket@support.mailvoid.com', 'Application crashes when uploading large files.', 0, 'user@testing.com', 'Beta Tester', NULL, 'Bug Report - Upload Crash', 'utf-8', DATE_SUB(NOW(), INTERVAL 4 HOUR), 'subdomain/support'),
(42, 'urgent@support.mailvoid.com', 'URGENT: Cannot access production data!', 0, 'enterprise@client.com', 'Enterprise Client', NULL, 'URGENT: Data Access Issue', 'utf-8', DATE_SUB(NOW(), INTERVAL 30 MINUTE), 'subdomain/support');

-- Sales group emails (manager owns, no one else shared)
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(50, 'inquiry@sales.mailvoid.com', 'Interested in the enterprise plan for 500 seats.', 0, 'ceo@startup.com', 'Startup CEO', NULL, 'Enterprise Plan Inquiry', 'utf-8', DATE_SUB(NOW(), INTERVAL 12 HOUR), 'subdomain/sales'),
(51, 'quote@sales.mailvoid.com', 'Please send a quote for annual licensing.', 0, 'procurement@bigcorp.com', 'Big Corp Procurement', NULL, 'Quote Request - Annual License', 'utf-8', DATE_SUB(NOW(), INTERVAL 6 HOUR), 'subdomain/sales');

-- Marketing group emails (jane owns, alice shared)
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(60, 'campaign@marketing.mailvoid.com', '<h1>New Product Launch!</h1><p>Announcing our latest feature.</p>', 1, 'noreply@company.com', 'Company Notifications', NULL, 'Product Launch Announcement', 'utf-8', DATE_SUB(NOW(), INTERVAL 10 HOUR), 'subdomain/marketing'),
(61, 'analytics@marketing.mailvoid.com', 'Monthly campaign analytics are ready.', 0, 'analytics@tools.com', 'Analytics Service', NULL, 'Monthly Analytics Report', 'utf-8', DATE_SUB(NOW(), INTERVAL 2 DAY), 'subdomain/marketing');

-- Dev group emails (john owns, manager shared with themselves)
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(70, 'build@dev.mailvoid.com', 'Build #1234 failed. Check logs for details.', 0, 'ci@buildserver.com', 'Build Server', NULL, 'Build Failed - #1234', 'utf-8', DATE_SUB(NOW(), INTERVAL 3 HOUR), 'subdomain/dev'),
(71, 'deploy@dev.mailvoid.com', 'Production deployment v2.5.1 successful.', 0, 'deploy@automation.com', 'Deployment Bot', NULL, 'Deploy Successful - v2.5.1', 'utf-8', DATE_SUB(NOW(), INTERVAL 1 HOUR), 'subdomain/dev'),
(72, 'security@dev.mailvoid.com', 'Critical vulnerability found in lodash@4.17.20.', 0, 'security@alerts.com', 'Security Alerts', NULL, 'Security Alert - lodash CVE', 'utf-8', DATE_SUB(NOW(), INTERVAL 45 MINUTE), 'subdomain/dev');

-- Staging group emails (john owns, jane shared)
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(80, 'test@staging.mailvoid.com', 'All 142 integration tests passed.', 0, 'ci@buildserver.com', 'Build Server', NULL, 'Test Results - All Passed', 'utf-8', DATE_SUB(NOW(), INTERVAL 20 MINUTE), 'subdomain/staging'),
(81, 'alerts@staging.mailvoid.com', 'Memory usage exceeded 90% threshold.', 0, 'monitoring@infra.com', 'Infrastructure Monitor', NULL, 'Staging Alert - High Memory', 'utf-8', DATE_SUB(NOW(), INTERVAL 10 MINUTE), 'subdomain/staging');

-- Some read status entries
INSERT INTO UserMailRead (UserId, MailId, ReadAt) VALUES
('11111111-1111-1111-1111-111111111111', 10, NOW()),
('11111111-1111-1111-1111-111111111111', 40, NOW()),
('22222222-2222-2222-2222-222222222222', 20, NOW()),
('33333333-3333-3333-3333-333333333333', 70, NOW());

-- ===========================
-- VERIFICATION QUERIES
-- ===========================
-- SELECT 'Users' as Info, COUNT(*) as Count FROM `User`;
-- SELECT 'Mail Groups' as Info, COUNT(*) as Count FROM MailGroup;
-- SELECT 'Emails' as Info, COUNT(*) as Count FROM Mail;
-- SELECT 'Shared Access' as Info, COUNT(*) as Count FROM MailGroupUser;

-- ===========================
-- TEST SCENARIOS
-- ===========================
/*
ACCOUNTS (all test users use password: "password123"):
  - admin/admin (admin) - sees base domain emails, can access hooks
  - john/password123 (user) - owns dev & staging groups
  - jane/password123 (user) - owns marketing group
  - manager/password123 (admin) - owns support & sales groups, can access hooks
  - alice/password123 (user)

VISIBILITY:
  - admin: own private mailbox + base domain (default) emails
  - john: own mailbox + dev (owner) + staging (owner) + support (shared)
  - jane: own mailbox + marketing (owner) + support (shared) + staging (shared)
  - manager: own mailbox + support (owner) + sales (owner) + dev (shared) + base domain (admin)
  - alice: own mailbox + marketing (shared)

KEY TEST SCENARIOS:
  1. Admin does NOT auto-see all subdomains (only base domain + own/shared)
  2. Admin (manager) shared dev with themselves to opt-in
  3. Any user with access can set retention on a shared mailbox
  4. Any user with access can share a mailbox with others
  5. Hooks feature only visible to admin users (admin, manager)
  6. Non-admin users cannot navigate to /hooks
  7. Delete only available to mailbox owners
*/
