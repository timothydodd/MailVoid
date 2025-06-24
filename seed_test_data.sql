-- MailVoid Test Data Seed Script
-- This script creates sample data for testing the MailVoid application functionality
-- Run this after the initial migration has been applied

-- Note: Password hashes are for "password123" - use this to login to test accounts
-- The admin user (admin/admin) is already created by the application startup

-- Clear existing test data (keep admin user)
DELETE FROM MailGroupUser WHERE UserId != (SELECT Id FROM User WHERE UserName = 'admin');
DELETE FROM RefreshToken WHERE UserId != (SELECT Id FROM User WHERE UserName = 'admin');
DELETE FROM Mail;
DELETE FROM Contact;
DELETE FROM MailGroup WHERE OwnerUserId != (SELECT Id FROM User WHERE UserName = 'admin');
DELETE FROM User WHERE UserName != 'admin';

-- ===========================
-- SAMPLE USERS
-- ===========================

-- Test User 1 - Regular user (john)
INSERT INTO User (Id, UserName, PasswordHash, TimeStamp, Role) VALUES
('11111111-1111-1111-1111-111111111111', 'john', 'AQAAAAIAAYagAAAAEKj8QkBQqKqJ+VQQ6IzP4H4aZZOQtN4Zj8gJKf+A5gHxXzFQJ8LHOP5N9eHgYjO9Hw==', NOW(), 0);

-- Test User 2 - Regular user (jane)
INSERT INTO User (Id, UserName, PasswordHash, TimeStamp, Role) VALUES
('22222222-2222-2222-2222-222222222222', 'jane', 'AQAAAAIAAYagAAAAEKj8QkBQqKqJ+VQQ6IzP4H4aZZOQtN4Zj8gJKf+A5gHxXzFQJ8LHOP5N9eHgYjO9Hw==', NOW(), 0);

-- Test User 3 - Admin user (manager)
INSERT INTO User (Id, UserName, PasswordHash, TimeStamp, Role) VALUES
('33333333-3333-3333-3333-333333333333', 'manager', 'AQAAAAIAAYagAAAAEKj8QkBQqKqJ+VQQ6IzP4H4aZZOQtN4Zj8gJKf+A5gHxXzFQJ8LHOP5N9eHgYjO9Hw==', NOW(), 1);

-- Test User 4 - Regular user (alice)
INSERT INTO User (Id, UserName, PasswordHash, TimeStamp, Role) VALUES
('44444444-4444-4444-4444-444444444444', 'alice', 'AQAAAAIAAYagAAAAEKj8QkBQqKqJ+VQQ6IzP4H4aZZOQtN4Zj8gJKf+A5gHxXzFQJ8LHOP5N9eHgYjO9Hw==', NOW(), 0);

-- ===========================
-- MAIL GROUPS
-- ===========================

-- Default private mailboxes for each user (these are auto-created but we'll add them manually for testing)
INSERT INTO MailGroup (Id, Path, Subdomain, Description, OwnerUserId, IsPublic, IsUserPrivate, IsDefaultMailbox, CreatedAt) VALUES
(100, 'user-john', 'john', 'Private mailbox for john', '11111111-1111-1111-1111-111111111111', 0, 1, 1, NOW()),
(101, 'user-jane', 'jane', 'Private mailbox for jane', '22222222-2222-2222-2222-222222222222', 0, 1, 1, NOW()),
(102, 'user-manager', 'manager', 'Private mailbox for manager', '33333333-3333-3333-3333-333333333333', 0, 1, 1, NOW()),
(103, 'user-alice', 'alice', 'Private mailbox for alice', '44444444-4444-4444-4444-444444444444', 0, 1, 1, NOW());

-- Public mail groups for different subdomains
INSERT INTO MailGroup (Id, Path, Subdomain, Description, OwnerUserId, IsPublic, IsUserPrivate, IsDefaultMailbox, CreatedAt) VALUES
(200, 'support', 'support', 'Customer support emails', '33333333-3333-3333-3333-333333333333', 1, 0, 0, NOW()),
(201, 'sales', 'sales', 'Sales inquiries', '33333333-3333-3333-3333-333333333333', 1, 0, 0, NOW()),
(202, 'marketing', 'marketing', 'Marketing campaigns', '22222222-2222-2222-2222-222222222222', 1, 0, 0, NOW()),
(203, 'dev', 'dev', 'Development team emails', '11111111-1111-1111-1111-111111111111', 0, 0, 0, NOW()),
(204, 'test', 'test', 'Testing environment emails', '11111111-1111-1111-1111-111111111111', 1, 0, 0, NOW());

-- Additional private mailboxes (claimed by users)
INSERT INTO MailGroup (Id, Path, Subdomain, Description, OwnerUserId, IsPublic, IsUserPrivate, IsDefaultMailbox, CreatedAt) VALUES
(300, 'user-jane', 'newsletter', 'Newsletter subscription mailbox', '22222222-2222-2222-2222-222222222222', 0, 1, 0, NOW()),
(301, 'user-alice', 'personal', 'Personal project emails', '44444444-4444-4444-4444-444444444444', 0, 1, 0, NOW());

-- ===========================
-- MAIL GROUP USERS (Access permissions)
-- ===========================

-- Give john access to dev group (he owns it, but let's add explicit access for testing)
INSERT INTO MailGroupUser (Id, MailGroupId, UserId, GrantedAt) VALUES
(1, 203, '11111111-1111-1111-1111-111111111111', NOW());

-- Give jane access to support group
INSERT INTO MailGroupUser (Id, MailGroupId, UserId, GrantedAt) VALUES
(2, 200, '22222222-2222-2222-2222-222222222222', NOW());

-- Give alice access to marketing group
INSERT INTO MailGroupUser (Id, MailGroupId, UserId, GrantedAt) VALUES
(3, 202, '44444444-4444-4444-4444-444444444444', NOW());

-- Give manager access to dev group
INSERT INTO MailGroupUser (Id, MailGroupId, UserId, GrantedAt) VALUES
(4, 203, '33333333-3333-3333-3333-333333333333', NOW());

-- ===========================
-- CONTACTS
-- ===========================

INSERT INTO Contact (Id, `From`, Name) VALUES
(1, 'noreply@company.com', 'Company Notifications'),
(2, 'support@helpdesk.com', 'Help Desk'),
(3, 'newsletter@marketing.com', 'Marketing Newsletter'),
(4, 'admin@system.com', 'System Administrator'),
(5, 'orders@ecommerce.com', 'Order Processing'),
(6, 'security@alerts.com', 'Security Alerts'),
(7, 'team@development.com', 'Development Team'),
(8, 'billing@finance.com', 'Billing Department');

-- ===========================
-- SAMPLE EMAILS
-- ===========================

-- Emails to John's default mailbox
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(1, 'welcome@john.mailvoid.com', 'Welcome to MailVoid! This is your default mailbox.', 1, 'admin@mailvoid.com', 'MailVoid Admin', NULL, 'Welcome to MailVoid', 'utf-8', DATE_SUB(NOW(), INTERVAL 5 DAY), 'user-john'),
(2, 'notification@john.mailvoid.com', 'Your account has been successfully created.', 0, 'noreply@company.com', 'Company Notifications', NULL, 'Account Created Successfully', 'utf-8', DATE_SUB(NOW(), INTERVAL 4 DAY), 'user-john'),
(3, 'update@john.mailvoid.com', 'System maintenance scheduled for tonight.', 0, 'admin@system.com', 'System Administrator', NULL, 'Maintenance Schedule', 'utf-8', DATE_SUB(NOW(), INTERVAL 2 DAY), 'user-john');

-- Emails to Jane's default mailbox
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(4, 'info@jane.mailvoid.com', 'Hello Jane! Welcome to your personal mailbox.', 1, 'admin@mailvoid.com', 'MailVoid Admin', NULL, 'Personal Mailbox Setup', 'utf-8', DATE_SUB(NOW(), INTERVAL 3 DAY), 'user-jane'),
(5, 'newsletter@jane.mailvoid.com', 'Monthly newsletter with updates and tips.', 1, 'newsletter@marketing.com', 'Marketing Newsletter', NULL, 'Monthly Newsletter - January', 'utf-8', DATE_SUB(NOW(), INTERVAL 1 DAY), 'user-jane');

-- Emails to Jane's claimed newsletter mailbox
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(6, 'subscribe@newsletter.mailvoid.com', 'Thank you for subscribing to our newsletter!', 1, 'newsletter@marketing.com', 'Marketing Newsletter', NULL, 'Subscription Confirmed', 'utf-8', DATE_SUB(NOW(), INTERVAL 6 HOUR), 'user-jane'),
(7, 'promo@newsletter.mailvoid.com', 'Special promotion just for you! 50% off everything.', 1, 'orders@ecommerce.com', 'Order Processing', NULL, 'Special Promotion Alert', 'utf-8', DATE_SUB(NOW(), INTERVAL 2 HOUR), 'user-jane');

-- Emails to support group (public)
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(8, 'help@support.mailvoid.com', 'I need help with my account login.', 0, 'customer1@example.com', 'John Customer', NULL, 'Login Issue', 'utf-8', DATE_SUB(NOW(), INTERVAL 8 HOUR), 'support'),
(9, 'ticket@support.mailvoid.com', 'Bug report: Application crashes on startup.', 0, 'user@testing.com', 'Beta Tester', NULL, 'Bug Report - Crash on Startup', 'utf-8', DATE_SUB(NOW(), INTERVAL 4 HOUR), 'support'),
(10, 'urgent@support.mailvoid.com', 'URGENT: Cannot access my data!', 0, 'enterprise@client.com', 'Enterprise Client', NULL, 'URGENT: Data Access Issue', 'utf-8', DATE_SUB(NOW(), INTERVAL 30 MINUTE), 'support');

-- Emails to sales group (public)
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(11, 'inquiry@sales.mailvoid.com', 'I am interested in your enterprise plan.', 0, 'ceo@startup.com', 'Startup CEO', NULL, 'Enterprise Plan Inquiry', 'utf-8', DATE_SUB(NOW(), INTERVAL 12 HOUR), 'sales'),
(12, 'quote@sales.mailvoid.com', 'Please provide a quote for 1000 users.', 0, 'procurement@bigcorp.com', 'Big Corp Procurement', NULL, 'Quote Request - 1000 Users', 'utf-8', DATE_SUB(NOW(), INTERVAL 6 HOUR), 'sales'),
(13, 'demo@sales.mailvoid.com', 'Can we schedule a product demo?', 0, 'manager@company.com', 'Product Manager', NULL, 'Demo Request', 'utf-8', DATE_SUB(NOW(), INTERVAL 1 HOUR), 'sales');

-- Emails to marketing group (public)
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(14, 'campaign@marketing.mailvoid.com', '<h1>New Product Launch!</h1><p>We are excited to announce our new product.</p>', 1, 'team@development.com', 'Development Team', NULL, 'New Product Launch Announcement', 'utf-8', DATE_SUB(NOW(), INTERVAL 10 HOUR), 'marketing'),
(15, 'analytics@marketing.mailvoid.com', 'Monthly analytics report attached.', 0, 'analytics@tools.com', 'Analytics Service', NULL, 'Monthly Analytics Report', 'utf-8', DATE_SUB(NOW(), INTERVAL 2 DAY), 'marketing');

-- Emails to dev group (private)
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(16, 'build@dev.mailvoid.com', 'Build #1234 failed. Check the logs for details.', 0, 'ci@build.com', 'Build Server', NULL, 'Build Failed - #1234', 'utf-8', DATE_SUB(NOW(), INTERVAL 3 HOUR), 'dev'),
(17, 'deploy@dev.mailvoid.com', 'Production deployment successful.', 0, 'deploy@automation.com', 'Deployment Bot', NULL, 'Deployment Successful', 'utf-8', DATE_SUB(NOW(), INTERVAL 1 HOUR), 'dev'),
(18, 'security@dev.mailvoid.com', 'Security vulnerability detected in dependency.', 0, 'security@alerts.com', 'Security Alerts', NULL, 'Security Alert - Dependency Vulnerability', 'utf-8', DATE_SUB(NOW(), INTERVAL 45 MINUTE), 'dev');

-- Emails to test group (public)
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(19, 'automated@test.mailvoid.com', 'All tests passed successfully.', 0, 'testing@automation.com', 'Test Suite', NULL, 'Test Results - All Passed', 'utf-8', DATE_SUB(NOW(), INTERVAL 20 MINUTE), 'test'),
(20, 'load@test.mailvoid.com', 'Load testing completed. Performance metrics attached.', 0, 'performance@tools.com', 'Performance Testing', NULL, 'Load Test Results', 'utf-8', DATE_SUB(NOW(), INTERVAL 10 MINUTE), 'test');

-- Emails to Alice's claimed personal mailbox
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(21, 'project@personal.mailvoid.com', 'Your project proposal has been approved!', 1, 'manager@company.com', 'Project Manager', NULL, 'Project Proposal Approved', 'utf-8', DATE_SUB(NOW(), INTERVAL 5 HOUR), 'user-alice'),
(22, 'freelance@personal.mailvoid.com', 'Payment for last months work has been processed.', 0, 'billing@finance.com', 'Billing Department', NULL, 'Payment Processed', 'utf-8', DATE_SUB(NOW(), INTERVAL 3 DAY), 'user-alice');

-- Some unclaimed emails (for testing claiming functionality)
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(23, 'info@unclaimed.mailvoid.com', 'This email is in an unclaimed subdomain.', 0, 'sender@example.com', 'Unknown Sender', NULL, 'Unclaimed Email 1', 'utf-8', NOW(), 'unclaimed'),
(24, 'contact@available.mailvoid.com', 'Another unclaimed email for testing.', 0, 'test@example.com', 'Test Sender', NULL, 'Available for Claiming', 'utf-8', NOW(), 'available'),
(25, 'hello@open.mailvoid.com', 'This subdomain is open for claiming.', 0, 'demo@example.com', 'Demo User', NULL, 'Open Subdomain', 'utf-8', NOW(), 'open');

-- ===========================
-- VERIFICATION QUERIES
-- ===========================
-- Run these to verify the data was inserted correctly:

-- SELECT 'Users Created' as Info, COUNT(*) as Count FROM User;
-- SELECT 'Mail Groups Created' as Info, COUNT(*) as Count FROM MailGroup;
-- SELECT 'Emails Created' as Info, COUNT(*) as Count FROM Mail;
-- SELECT 'Contacts Created' as Info, COUNT(*) as Count FROM Contact;

-- Check default mailboxes:
-- SELECT u.UserName, mg.Path, mg.IsDefaultMailbox, mg.IsUserPrivate 
-- FROM MailGroup mg 
-- JOIN User u ON mg.OwnerUserId = u.Id 
-- WHERE mg.IsUserPrivate = 1;

-- Check email distribution:
-- SELECT MailGroupPath, COUNT(*) as EmailCount 
-- FROM Mail 
-- GROUP BY MailGroupPath 
-- ORDER BY EmailCount DESC;

-- ===========================
-- TEST SCENARIOS
-- ===========================
/*
This seed data creates the following test scenarios:

1. USER ACCOUNTS (All use password: "password123"):
   - admin/admin (existing admin user)
   - john/password123 (regular user)
   - jane/password123 (regular user)
   - manager/password123 (admin user)
   - alice/password123 (regular user)

2. DEFAULT MAILBOXES (Cannot be unclaimed):
   - john has: user-john (john subdomain)
   - jane has: user-jane (jane subdomain)
   - manager has: user-manager (manager subdomain)
   - alice has: user-alice (alice subdomain)

3. CLAIMED MAILBOXES (Can be unclaimed):
   - jane claimed: newsletter subdomain
   - alice claimed: personal subdomain

4. PUBLIC MAIL GROUPS:
   - support (owned by manager)
   - sales (owned by manager)
   - marketing (owned by jane)
   - test (owned by john)

5. PRIVATE MAIL GROUPS:
   - dev (owned by john, manager has access)

6. UNCLAIMED SUBDOMAINS (Available for claiming):
   - unclaimed
   - available
   - open

7. TESTING SCENARIOS:
   - Login with different user roles
   - View default mailboxes (should not show unclaim button)
   - View claimed mailboxes (should show unclaim button)
   - Try to claim unclaimed subdomains
   - Try to unclaim default mailboxes (should fail)
   - Test mail group access permissions
   - Test mail filtering and grouping
   - Test 404 redirect functionality
*/