# MailVoid Testing Guide

This guide explains how to use the provided test data to thoroughly test the MailVoid application functionality.

## Setup Instructions

### 1. Run the Seed Script

After your database is set up and migrations are applied, run the seed script:

```bash
# Connect to your MySQL database
mysql -u your_username -p your_database_name < seed_test_data.sql
```

Or using MySQL Workbench:
1. Open MySQL Workbench
2. Connect to your database
3. Open the `seed_test_data.sql` file
4. Execute the script

### 2. Test Accounts

The script creates the following test accounts (password: `password123` for all except admin):

| Username | Password | Role | Default Mailbox |
|----------|----------|------|-----------------|
| admin | admin | Admin | admin@admin.mailvoid.com |
| john | password123 | User | *@john.mailvoid.com |
| jane | password123 | User | *@jane.mailvoid.com |
| manager | password123 | Admin | *@manager.mailvoid.com |
| alice | password123 | User | *@alice.mailvoid.com |

## Test Scenarios

### 1. Authentication & User Management
- [ ] Login with each test account
- [ ] Verify role-based access (admin vs user)
- [ ] Test password changes
- [ ] Test logout functionality

### 2. Default Mailbox Protection
- [ ] Login as any user
- [ ] Go to "Manage Mailboxes" in Mail Settings
- [ ] Verify default mailbox shows "Default" badge
- [ ] Verify no "Release" button on default mailbox
- [ ] Try API call to unclaim default mailbox (should fail)

### 3. Mail Group Functionality

#### Public Groups (Everyone can see):
- [ ] **support**: Customer support emails (owned by manager)
- [ ] **sales**: Sales inquiries (owned by manager) 
- [ ] **marketing**: Marketing campaigns (owned by jane)
- [ ] **test**: Testing emails (owned by john)

#### Private Groups (Owner + granted users only):
- [ ] **dev**: Development emails (owned by john, manager has access)

#### Test Steps:
- [ ] Login as different users and verify which groups are visible
- [ ] Test group access permissions
- [ ] Try accessing emails in groups without permission
- [ ] Test group management (add/remove users, change settings)

### 4. Email Claiming/Unclaiming

#### Claimed Subdomains (Can be unclaimed):
- [ ] jane claimed: `newsletter` subdomain
- [ ] alice claimed: `personal` subdomain

#### Available Subdomains (Can be claimed):
- [ ] `unclaimed` subdomain
- [ ] `available` subdomain  
- [ ] `open` subdomain

#### Test Steps:
- [ ] View claimed mailboxes (should show "Release" button)
- [ ] Successfully unclaim non-default mailboxes
- [ ] Claim available subdomains
- [ ] Verify emails move between groups when claiming/unclaiming

### 5. Email Viewing & Management
- [ ] View emails in different groups
- [ ] Test email filtering and search
- [ ] Test email deletion
- [ ] Test HTML vs text email rendering
- [ ] Check email timestamps and sorting

### 6. 404 Error Handling
- [ ] Make an invalid API request
- [ ] Verify automatic logout and redirect to login
- [ ] Test with expired/invalid tokens

### 7. Mail Group Settings
- [ ] Update group descriptions
- [ ] Change group visibility (public/private)
- [ ] Grant/revoke user access to groups
- [ ] Verify permission restrictions on private groups

## Email Distribution

The seed data creates emails distributed as follows:

| Mail Group | Email Count | Description |
|------------|-------------|-------------|
| user-john | 3 | John's default mailbox |
| user-jane | 2 | Jane's default mailbox |
| user-jane | 2 | Jane's claimed newsletter emails |
| user-alice | 2 | Alice's claimed personal emails |
| support | 3 | Public support tickets |
| sales | 3 | Public sales inquiries |
| marketing | 2 | Public marketing emails |
| dev | 3 | Private development emails |
| test | 2 | Public test emails |
| unclaimed | 1 | Available for claiming |
| available | 1 | Available for claiming |
| open | 1 | Available for claiming |

## Expected Behaviors

### ✅ Should Work:
- Login with any test account
- View emails in permitted groups
- Claim unclaimed subdomains
- Unclaim non-default mailboxes
- Admin users can manage all groups
- Users can manage their own groups

### ❌ Should Fail:
- Unclaiming default mailboxes
- Accessing private groups without permission
- Invalid login attempts
- Modifying groups owned by others (non-admin)

## Cleanup

To reset the test data, simply run the seed script again. It includes cleanup statements at the beginning.

## API Testing

For API testing, you can use these endpoints with proper authentication:

```bash
# Get user's private mailboxes
GET /api/mail/my-mailboxes

# Try to unclaim default mailbox (should fail)
DELETE /api/mail/unclaim
Body: {"emailAddress": "anything@john.mailvoid.com"}

# Claim available subdomain
POST /api/mail/claim  
Body: {"emailAddress": "test@unclaimed.mailvoid.com"}

# Check if email is claimed
GET /api/mail/check/test@unclaimed.mailvoid.com
```

This comprehensive test suite will help you verify all the functionality works correctly!