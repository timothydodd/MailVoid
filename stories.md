# User Stories

## Story 1: Automatic Mailbox Cleaning

### Overview
As a user, I want to configure automatic cleaning of old emails from my mailboxes so that my storage remains manageable and I don't accumulate outdated test emails.

### Acceptance Criteria
1. Each mailbox can have a retention period configured (in days)
2. Default retention period is 3 days for new mailboxes
3. Emails older than the retention period are automatically deleted
4. Users can disable auto-cleaning by setting retention to 0 or null
5. Cleaning process runs as a background job
6. Users receive notification when emails are auto-deleted (optional)

### Technical Implementation

#### Backend Changes
1. **Database Schema**
   - Add `RetentionDays` field to `MailGroup` model (nullable int, default: 3)
   - Add migration to update existing mailboxes with default value

2. **Background Service**
   - Create `MailCleanupService` as a hosted service
   - Run cleanup job every hour (configurable)
   - Query emails older than retention period per mailbox
   - Batch delete operations for performance
   - Log cleanup activities

3. **API Endpoints**
   - `PUT /api/mail/mailbox/{id}/retention` - Update retention settings
   - `GET /api/mail/mailbox/{id}/retention` - Get current retention settings

#### Frontend Changes
1. **Mailbox Settings**
   - Add retention settings to mailbox configuration
   - Dropdown or input for days (0-365)
   - Show "No auto-delete" when set to 0
   - Save button to update settings

2. **Visual Indicators**
   - Show retention period in mailbox info
   - Warning when viewing emails near deletion
   - Toast notification after cleanup (if enabled)

---

## Story 2: Mark All Emails as Read

### Overview
As a user, I want to mark all emails in a mailbox as read at once so that I can quickly clear unread notifications without clicking each email individually.

### Acceptance Criteria
1. "Mark all as read" option available in mailbox dropdown menu
2. Action only affects emails in the selected mailbox
3. Updates unread count immediately
4. Confirmation dialog for the action
5. Works with both public and private mailboxes
6. Respects user permissions (only marks emails user has access to)

### Technical Implementation

#### Backend Changes
1. **API Endpoint**
   - `POST /api/mail/mark-all-read`
   - Request body: `{ mailboxPath: string }`
   - Bulk insert into `UserMailRead` table
   - Return updated unread counts

2. **Service Method**
   - Add to `MailService` or create `MailBulkOperationService`
   - Efficient bulk insert using Entity Framework
   - Handle duplicate read records gracefully
   - Update mailbox last activity timestamp

#### Frontend Changes
1. **Box Menu Component**
   - Add "Mark all as read" option to dropdown
   - Show divider between actions
   - Add icon (e.g., check-circle)

2. **Functionality**
   - Confirmation dialog: "Mark all emails in [mailbox] as read?"
   - Call API endpoint on confirmation
   - Update local state/cache
   - Refresh unread counts
   - Show success toast

3. **UI Updates**
   - Disable option if no unread emails
   - Loading state during operation
   - Error handling with retry option

### Alternative Implementation
Instead of dropdown menu, could add a toolbar button when viewing a mailbox:
- Button appears in mail list header
- More discoverable for users
- Can combine with other bulk actions later