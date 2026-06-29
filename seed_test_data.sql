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

-- Rich HTML emails for admin viewing/rendering tests
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
-- Transactional receipt with inline-styled table
(4, 'orders@mailvoid.com', '<!DOCTYPE html><html><body style="font-family:Arial,sans-serif;background:#f5f5f5;padding:20px;margin:0;"><table cellpadding="0" cellspacing="0" border="0" width="600" style="background:#ffffff;border-radius:8px;margin:0 auto;"><tr><td style="background:#4f46e5;padding:24px;border-radius:8px 8px 0 0;color:#ffffff;font-size:24px;font-weight:bold;">Order Confirmation</td></tr><tr><td style="padding:24px;color:#333;"><p>Hi Alex,</p><p>Thanks for your order! Your receipt is below.</p><table width="100%" cellpadding="8" cellspacing="0" border="0" style="border-collapse:collapse;margin-top:16px;"><thead><tr style="background:#f3f4f6;text-align:left;"><th>Item</th><th>Qty</th><th style="text-align:right;">Price</th></tr></thead><tbody><tr style="border-bottom:1px solid #e5e7eb;"><td>Widget Pro (Annual)</td><td>1</td><td style="text-align:right;">$199.00</td></tr><tr style="border-bottom:1px solid #e5e7eb;"><td>Premium Support</td><td>1</td><td style="text-align:right;">$49.00</td></tr><tr><td colspan="2" style="text-align:right;font-weight:bold;padding-top:16px;">Total</td><td style="text-align:right;font-weight:bold;padding-top:16px;">$248.00</td></tr></tbody></table><p style="margin-top:24px;"><a href="https://example.com/receipt/12345" style="background:#4f46e5;color:#ffffff;padding:12px 24px;text-decoration:none;border-radius:6px;display:inline-block;">View Receipt</a></p></td></tr><tr><td style="padding:16px 24px;color:#9ca3af;font-size:12px;text-align:center;border-top:1px solid #e5e7eb;">Acme Inc &middot; 123 Main St &middot; <a href="https://example.com/unsubscribe" style="color:#9ca3af;">Unsubscribe</a></td></tr></table></body></html>', 1, 'orders@ecommerce.com', 'Order Processing', NULL, 'Order #12345 confirmed - $248.00', 'utf-8', DATE_SUB(NOW(), INTERVAL 3 HOUR), 'subdomain/default'),

-- Marketing-style email with images and CTA
(5, 'news@mailvoid.com', '<!DOCTYPE html><html><body style="margin:0;padding:0;background:#0f172a;font-family:Helvetica,Arial,sans-serif;"><table width="100%" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" style="padding:32px 16px;"><table width="600" cellpadding="0" cellspacing="0" border="0" style="background:#ffffff;border-radius:12px;overflow:hidden;"><tr><td style="background:linear-gradient(135deg,#6366f1,#a855f7);padding:48px 32px;text-align:center;"><h1 style="color:#ffffff;font-size:32px;margin:0;">Big News &#127881;</h1><p style="color:#e0e7ff;margin:12px 0 0;font-size:16px;">We just shipped something amazing</p></td></tr><tr><td style="padding:32px;color:#1f2937;"><h2 style="margin-top:0;">Introducing Project Phoenix</h2><p>Three years in the making, Project Phoenix brings together everything you''ve been asking for: faster sync, smarter search, and a redesigned dashboard.</p><ul><li>10x faster indexing</li><li>Real-time collaboration</li><li>End-to-end encryption</li></ul><p style="text-align:center;margin:32px 0;"><a href="https://example.com/launch" style="background:#6366f1;color:#ffffff;padding:14px 32px;text-decoration:none;border-radius:8px;font-weight:bold;display:inline-block;">Try it free</a></p></td></tr><tr><td style="background:#f9fafb;padding:24px;text-align:center;color:#6b7280;font-size:13px;">You''re receiving this because you signed up at example.com</td></tr></table></td></tr></table></body></html>', 1, 'team@productco.com', 'Product Team', NULL, 'Big News: Project Phoenix is here', 'utf-8', DATE_SUB(NOW(), INTERVAL 18 HOUR), 'subdomain/default'),

-- Password reset (simple but realistic transactional)
(6, 'security@mailvoid.com', '<!DOCTYPE html><html><body style="font-family:-apple-system,BlinkMacSystemFont,sans-serif;color:#222;max-width:540px;margin:40px auto;padding:0 20px;"><h2 style="color:#dc2626;">Password reset requested</h2><p>Hi there,</p><p>We received a request to reset the password for your account. Click the button below to set a new password. This link will expire in <strong>30 minutes</strong>.</p><p><a href="https://example.com/reset?token=eyJhbGciOi" style="background:#111827;color:#fff;padding:12px 24px;border-radius:6px;text-decoration:none;display:inline-block;">Reset password</a></p><p style="color:#6b7280;font-size:14px;">If you didn''t request this, you can safely ignore this email &mdash; your password won''t change.</p><hr style="border:none;border-top:1px solid #e5e7eb;margin:32px 0;"><p style="color:#9ca3af;font-size:12px;">Sent from a no-reply address. Replies are not monitored.</p></body></html>', 1, 'noreply@authservice.com', 'Auth Service', NULL, 'Reset your password', 'utf-8', DATE_SUB(NOW(), INTERVAL 45 MINUTE), 'subdomain/default'),

-- Dangerous content test: external image, script (should be sandboxed/stripped), iframe
(7, 'security-test@mailvoid.com', '<html><body><h1>Suspicious-looking email</h1><p>This message tests the renderer''s safety controls.</p><img src="https://tracker.example.com/pixel.gif?id=abc123" width="1" height="1" alt="" /><p>Click here to <a href="javascript:alert(1)">claim your prize</a>.</p><script>alert("XSS attempt");</script><iframe src="https://untrusted.example.com/" width="400" height="200"></iframe><p style="background:url(''javascript:alert(2)'')">Inline style attack</p><form action="https://phisher.example.com/steal" method="post"><input type="text" name="password" placeholder="Enter password"/><button>Submit</button></form></body></html>', 1, 'attacker@phishing-test.com', 'Phishing Sim', NULL, 'XSS / sandbox renderer test', 'utf-8', DATE_SUB(NOW(), INTERVAL 5 HOUR), 'subdomain/default'),

-- Plain text with very long subject + unicode (overflow + charset test)
(8, 'unicode-test@mailvoid.com', 'Hej! Detta är ett test med svenska tecken: åäö ÅÄÖ. 你好世界. مرحبا بالعالم. &#127757;', 0, 'globe@i18n.example.com', 'Internationalization Tester', NULL, 'A very long subject line designed to test overflow behavior in the email list — does it ellipsize properly when it goes well past the column width? &#127757; &#128640; &#9989;', 'utf-8', DATE_SUB(NOW(), INTERVAL 2 DAY), 'subdomain/default'),

-- Search-only-in-body test (subject/from don''t mention "watermelon")
(9, 'body-search@mailvoid.com', 'This is a multi-paragraph plain text email used to verify that searching across the body works correctly.\n\nKeyword to find: watermelon.\n\nIf the search feature only looks at the subject and from fields, this email will not be found when you search for "watermelon" — but it should be.', 0, 'qa@testteam.com', 'QA Team', NULL, 'Plain text body for search testing', 'utf-8', DATE_SUB(NOW(), INTERVAL 4 HOUR), 'subdomain/default');

-- Full-feature email: headers, raw source, attachments, SPF/DKIM (admin-visible)
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath,
                  Headers, RawSource, MessageId, SpfResult, DkimResult, Attachments)
VALUES (
  10,
  'fullfeature@mailvoid.com',
  '<p>This email exercises every persisted field: headers tab, raw source tab, attachments panel, and SPF/DKIM badges.</p><p>Open it on the admin account.</p>',
  1,
  'sender@trusted.example.com',
  'Trusted Sender',
  NULL,
  'Full-feature test email (headers, raw, attachments, badges)',
  'utf-8',
  DATE_SUB(NOW(), INTERVAL 1 HOUR),
  'subdomain/default',
  '{"From":"\"Trusted Sender\" <sender@trusted.example.com>","To":"fullfeature@mailvoid.com","Subject":"Full-feature test email","Date":"Mon, 09 May 2026 10:00:00 +0000","Message-ID":"<full-feature-001@trusted.example.com>","MIME-Version":"1.0","Content-Type":"multipart/mixed; boundary=\"BOUNDARY123\"","Authentication-Results":"mx.mailvoid.com; spf=pass smtp.mailfrom=trusted.example.com; dkim=pass header.d=trusted.example.com","Received-SPF":"pass (mailvoid.com: domain of trusted.example.com designates 192.0.2.10 as permitted sender)","DKIM-Signature":"v=1; a=rsa-sha256; c=relaxed/relaxed; d=trusted.example.com; s=mail; h=from:to:subject;"}',
  'Return-Path: <sender@trusted.example.com>\r\nReceived: from mx.trusted.example.com (mx.trusted.example.com [192.0.2.10])\r\n        by mail.mailvoid.com (Postfix) with ESMTPS id ABCDEF\r\n        for <fullfeature@mailvoid.com>; Mon, 09 May 2026 10:00:00 +0000\r\nFrom: "Trusted Sender" <sender@trusted.example.com>\r\nTo: fullfeature@mailvoid.com\r\nSubject: Full-feature test email\r\nDate: Mon, 09 May 2026 10:00:00 +0000\r\nMessage-ID: <full-feature-001@trusted.example.com>\r\nMIME-Version: 1.0\r\nContent-Type: multipart/mixed; boundary="BOUNDARY123"\r\nAuthentication-Results: mx.mailvoid.com; spf=pass smtp.mailfrom=trusted.example.com; dkim=pass header.d=trusted.example.com\r\n\r\n--BOUNDARY123\r\nContent-Type: text/html; charset=utf-8\r\n\r\n<p>This email exercises every persisted field.</p>\r\n\r\n--BOUNDARY123\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Disposition: attachment; filename="notes.txt"\r\n\r\nHello from the attachment.\r\n\r\n--BOUNDARY123--\r\n',
  '<full-feature-001@trusted.example.com>',
  'pass',
  'pass',
  '[{"filename":"notes.txt","contentType":"text/plain","sizeBytes":26,"content":"SGVsbG8gZnJvbSB0aGUgYXR0YWNobWVudC4="},{"filename":"hello.json","contentType":"application/json","sizeBytes":19,"content":"eyJoZWxsbyI6ICJ3b3JsZCJ9"}]'
);

-- Failing-auth test (admin-visible) — SPF fail, DKIM fail, no attachments
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath,
                  Headers, MessageId, SpfResult, DkimResult)
VALUES (
  11,
  'spamtest@mailvoid.com',
  'You have won a million dollars. Click here.',
  0,
  'spammer@suspicious.example.com',
  'Definitely Not Spam',
  NULL,
  'You have WON!!! Click NOW',
  'utf-8',
  DATE_SUB(NOW(), INTERVAL 2 HOUR),
  'subdomain/default',
  '{"From":"\"Definitely Not Spam\" <spammer@suspicious.example.com>","To":"spamtest@mailvoid.com","Subject":"You have WON!!! Click NOW","Authentication-Results":"mx.mailvoid.com; spf=fail smtp.mailfrom=suspicious.example.com; dkim=fail"}',
  '<spam-001@suspicious.example.com>',
  'fail',
  'fail'
);

-- John's private mailbox
INSERT INTO Mail (Id, `To`, Text, IsHtml, `From`, FromName, ToOthers, Subject, Charsets, CreatedOn, MailGroupPath) VALUES
(12, 'welcome@john.mailvoid.com', '<h2>Welcome!</h2><p>Your mailbox is ready.</p>', 1, 'noreply@company.com', 'Company Notifications', NULL, 'Welcome to MailVoid', 'utf-8', DATE_SUB(NOW(), INTERVAL 5 DAY), 'user-john'),
(13, 'alerts@john.mailvoid.com', 'Your CI pipeline has been configured.', 0, 'ci@buildserver.com', 'Build Server', NULL, 'Pipeline Setup Complete', 'utf-8', DATE_SUB(NOW(), INTERVAL 2 DAY), 'user-john');

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
('11111111-1111-1111-1111-111111111111', 12, NOW()),
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
