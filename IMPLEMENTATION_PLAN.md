# MailVoid Enhancement Plan

## Overview
Two-phase implementation:
- **Phase A**: Remove Entity Framework Core, migrate to RoboDodd.OrmLite (fresh DB start)
- **Phase B**: Add webhook capture feature

---

# Progress Tracker

## Phase A: EF → OrmLite Migration
- [x] A1: Add git submodule and update csproj
- [x] A2: Update model attributes (User, Mail, MailGroup, RefreshToken, UserMailRead, Contact)
- [x] A3: Create DatabaseService
- [x] A4: Update Program.cs
- [x] A5: Update controllers (AuthController, MailController, WebhookController)
- [x] A6: Update services (DatabaseInitializer, RefreshTokenService, MailGroupService, UserManagementService, MailCleanupService)
- [x] A7: Delete EF files (MailVoidDbContext, Migrations folder)
- [x] A8: Test - verify app runs with fresh DB

## Phase B: Webhook Capture Feature
- [x] B1: Create Webhook and WebhookBucket models
- [x] B2: Update DatabaseService to create webhook tables
- [x] B3: Create WebhookBucketService and WebhookCleanupService
- [x] B4: Create HooksController and WebhookManagementController
- [x] B5: Update SignalR service (frontend)
- [x] B6: Create webhook.service.ts (frontend)
- [x] B7: Create Hooks page components
- [x] B8: Create HookDetail page components
- [x] B9: Update routes and app config
- [x] B10: Test - verify webhook capture and viewing works

---

# PHASE A: Entity Framework to RoboDodd.OrmLite Migration

## A1. Add RoboDodd.OrmLite as Git Submodule

```bash
git submodule add https://github.com/timothydodd/RoboDodd.OrmLite.git src/RoboDodd.OrmLite
```

Update `src/MailVoidApi/MailVoidApi.csproj`:
- Add project reference to OrmLite
- Remove EF Core packages:
  - `Microsoft.EntityFrameworkCore`
  - `Microsoft.EntityFrameworkCore.Design`
  - `Pomelo.EntityFrameworkCore.MySql`

---

## A2. Update Model Attributes

Models need to use OrmLite attributes. Most are already compatible (`[Key]`, `[Required]`), but we need to add `[Table]` and `[Index]` attributes.

### Models to Update:

**`src/MailVoidApi/Models/User.cs`**
```csharp
[Table("User")]
[Index("IX_User_UserName", nameof(UserName), IsUnique = true)]
public class User
{
    [Key]
    public Guid Id { get; set; }
    [Required]
    public required string UserName { get; set; }
    [Required]
    public required string PasswordHash { get; set; }
    [Required]
    public required DateTime TimeStamp { get; set; }
    public Role Role { get; set; } = Role.User;
}
```

**`src/MailVoidApi/Models/Mail.cs`**
```csharp
[Table("Mail")]
[Index("IX_Mail_To", nameof(To))]
[Index("IX_Mail_From", nameof(From))]
[Index("IX_Mail_MailGroupPath", nameof(MailGroupPath))]
public class Mail
{
    [Key]
    public long Id { get; set; }
    [Required]
    public required string To { get; set; }
    [Required]
    public required string Text { get; set; }
    public bool IsHtml { get; set; }
    [Required]
    public required string From { get; set; }
    public string? FromName { get; set; }
    public string? ToOthers { get; set; }
    [Required]
    public required string Subject { get; set; }
    public string? Charsets { get; set; }
    [Default(DefaultType.CurrentTimestamp)]
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    public string? MailGroupPath { get; set; }
}
```

**`src/MailVoidApi/Models/MailGroup.cs`**
```csharp
[Table("MailGroup")]
[Index("IX_MailGroup_Path", nameof(Path))]
[Index("IX_MailGroup_Subdomain", nameof(Subdomain))]
public class MailGroup
{
    [Key]
    public long Id { get; set; }
    public string? Path { get; set; }
    public string? Subdomain { get; set; }
    public string? Description { get; set; }
    [Required]
    public required Guid OwnerUserId { get; set; }
    public bool IsPublic { get; set; }
    public bool IsUserPrivate { get; set; }
    public bool IsDefaultMailbox { get; set; }
    [Default(DefaultType.CurrentTimestamp)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastActivity { get; set; }
    public int? RetentionDays { get; set; } = 3;
}

[Table("MailGroupUser")]
[CompositeIndex("IX_MailGroupUser_Unique", nameof(MailGroupId), nameof(UserId), IsUnique = true)]
public class MailGroupUser
{
    [Key]
    public long Id { get; set; }
    [Required]
    public required long MailGroupId { get; set; }
    [Required]
    public required Guid UserId { get; set; }
    [Default(DefaultType.CurrentTimestamp)]
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
}
```

**`src/MailVoidApi/Models/RefreshToken.cs`**
```csharp
[Table("RefreshToken")]
[Index("IX_RefreshToken_Token", nameof(Token))]
[CompositeIndex("IX_RefreshToken_TokenUser", nameof(Token), nameof(UserId))]
public class RefreshToken
{
    [Key]
    public int Id { get; set; }
    [Required]
    public required string Token { get; set; }
    [Required]
    public required Guid UserId { get; set; }
    public DateTime ExpiryDate { get; set; }
    public bool IsRevoked { get; set; }
    [Default(DefaultType.CurrentTimestamp)]
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}
```

**`src/MailVoidApi/Models/UserMailRead.cs`**
```csharp
[Table("UserMailRead")]
[CompositeIndex("IX_UserMailRead_Unique", nameof(UserId), nameof(MailId), IsUnique = true)]
public class UserMailRead
{
    [Key]
    public long Id { get; set; }
    [Required]
    public required Guid UserId { get; set; }
    [Required]
    public required long MailId { get; set; }
    [Default(DefaultType.CurrentTimestamp)]
    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}
```

**`src/MailVoidApi/Models/Contact.cs`** (if exists)
```csharp
[Table("Contact")]
[Index("IX_Contact_From", nameof(From), IsUnique = true)]
public class Contact
{
    [Key]
    public long Id { get; set; }
    [Required]
    public required string From { get; set; }
    [Required]
    public required string Name { get; set; }
}
```

---

## A3. Create Database Service (replaces DbContext)

**New file: `src/MailVoidApi/Data/DatabaseService.cs`**

```csharp
using MySqlConnector;
using RoboDodd.OrmLite;

namespace MailVoidApi.Data;

public interface IDatabaseService
{
    Task<MySqlConnection> GetConnectionAsync();
    Task InitializeAsync();
}

public class DatabaseService : IDatabaseService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseService> _logger;

    public DatabaseService(IConfiguration configuration, ILogger<DatabaseService> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string not found");
        _logger = logger;
    }

    public async Task<MySqlConnection> GetConnectionAsync()
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing database tables...");
        using var db = await GetConnectionAsync();

        await db.CreateTableIfNotExistsAsync<User>();
        await db.CreateTableIfNotExistsAsync<Mail>();
        await db.CreateTableIfNotExistsAsync<MailGroup>();
        await db.CreateTableIfNotExistsAsync<MailGroupUser>();
        await db.CreateTableIfNotExistsAsync<RefreshToken>();
        await db.CreateTableIfNotExistsAsync<Contact>();
        await db.CreateTableIfNotExistsAsync<UserMailRead>();

        _logger.LogInformation("Database tables initialized");
    }
}
```

---

## A4. Update Program.cs

**File: `src/MailVoidApi/Program.cs`**

Remove:
- EF DbContext registration
- Migration check/apply code
- All `Microsoft.EntityFrameworkCore` usings

Add:
```csharp
builder.Services.AddSingleton<IDatabaseService, DatabaseService>();

// In startup:
using (var scope = app.Services.CreateScope())
{
    var dbService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
    await dbService.InitializeAsync();

    var dbInitializer = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await dbInitializer.SeedDefaultData();
}
```

---

## A5. Update Controllers (4 files)

### `src/MailVoidApi/Controllers/AuthController.cs`

Replace `MailVoidDbContext _context` with `IDatabaseService _db`

| EF Pattern | OrmLite Pattern |
|------------|-----------------|
| `_context.Users.FirstOrDefaultAsync(u => u.UserName == x)` | `db.SingleAsync<User>(u => u.UserName == x)` |
| `_context.Users.FindAsync(id)` | `db.SingleByIdAsync<User>(id)` |
| `_context.SaveChangesAsync()` | (not needed - operations auto-commit) |

### `src/MailVoidApi/Controllers/MailController.cs`

| EF Pattern | OrmLite Pattern |
|------------|-----------------|
| `_context.Mails.FindAsync(id)` | `db.SingleByIdAsync<Mail>(id)` |
| `_context.Mails.Where(x).ToListAsync()` | `db.SelectAsync<Mail>(x)` |
| `_context.MailGroups.FindAsync(id)` | `db.SingleByIdAsync<MailGroup>(id)` |
| `_context.Mails.Add(mail)` + `SaveChangesAsync()` | `db.InsertAsync(mail)` |
| `_context.Mails.Remove(mail)` + `SaveChangesAsync()` | `db.DeleteAsync(mail)` |
| `_context.Database.ExecuteSqlRawAsync(...)` | `db.ExecuteAsync(sql, params)` |

### `src/MailVoidApi/Controllers/WebhookController.cs`

Same patterns as above.

### `src/MailVoidApi/Controllers/UserManagementController.cs`

Delegates to `UserManagementService` - update service instead.

---

## A6. Update Services (5 files)

### `src/MailVoidApi/Services/DatabaseInitializer.cs`

```csharp
public class DatabaseInitializer
{
    private readonly IDatabaseService _db;
    private readonly PasswordService _passwordService;
    private readonly ILogger<DatabaseInitializer> _logger;

    public async Task SeedDefaultData()
    {
        using var db = await _db.GetConnectionAsync();
        var admin = await db.SingleAsync<User>(u => u.UserName == "admin");
        if (admin == null)
        {
            var user = new User { Id = Guid.NewGuid(), UserName = "admin", ... };
            await db.InsertAsync(user);
        }
    }
}
```

### `src/MailVoidApi/Services/RefreshTokenService.cs`

| EF Pattern | OrmLite Pattern |
|------------|-----------------|
| `_context.RefreshTokens.Add(token)` | `db.InsertAsync(token)` |
| `_context.RefreshTokens.FirstOrDefaultAsync(x => ...)` | `db.SingleAsync<RefreshToken>(x => ...)` |
| `_context.RefreshTokens.Where(...).ToListAsync()` | `db.SelectAsync<RefreshToken>(x => ...)` |
| `_context.RefreshTokens.RemoveRange(tokens)` | `db.DeleteAllAsync(tokens)` |

### `src/MailVoidApi/Services/MailGroupService.cs`

Same patterns. Note: No navigation properties - must do explicit joins or separate queries.

### `src/MailVoidApi/Services/UserManagementService.cs`

Same patterns.

### `src/MailVoidApi/Services/MailCleanupService.cs`

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        using var db = await _dbService.GetConnectionAsync();
        var groups = await db.SelectAsync<MailGroup>(g => g.RetentionDays != null && g.RetentionDays > 0);
        // ... cleanup logic
        await Task.Delay(_cleanupInterval, stoppingToken);
    }
}
```

---

## A7. Delete EF-Specific Files

- Delete: `src/MailVoidApi/Data/MailVoidDbContext.cs`
- Delete: `src/MailVoidApi/Migrations/` (entire folder)

---

## A8. Files Summary - Phase A

### New Files (2)
| File | Purpose |
|------|---------|
| `src/RoboDodd.OrmLite/` | Git submodule |
| `src/MailVoidApi/Data/DatabaseService.cs` | Connection management & table init |

### Modified Files (12)
| File | Changes |
|------|---------|
| `src/MailVoidApi/MailVoidApi.csproj` | Remove EF packages, add OrmLite reference |
| `src/MailVoidApi/Program.cs` | Replace EF setup with OrmLite |
| `src/MailVoidApi/Models/User.cs` | Add OrmLite attributes |
| `src/MailVoidApi/Models/Mail.cs` | Add OrmLite attributes |
| `src/MailVoidApi/Models/MailGroup.cs` | Add OrmLite attributes |
| `src/MailVoidApi/Models/RefreshToken.cs` | Add OrmLite attributes |
| `src/MailVoidApi/Models/UserMailRead.cs` | Add OrmLite attributes |
| `src/MailVoidApi/Controllers/AuthController.cs` | Use OrmLite queries |
| `src/MailVoidApi/Controllers/MailController.cs` | Use OrmLite queries |
| `src/MailVoidApi/Controllers/WebhookController.cs` | Use OrmLite queries |
| `src/MailVoidApi/Services/*.cs` (5 files) | Use OrmLite queries |

### Deleted Files
| File | Reason |
|------|--------|
| `src/MailVoidApi/Data/MailVoidDbContext.cs` | Replaced by DatabaseService |
| `src/MailVoidApi/Migrations/*` | Fresh start - OrmLite creates tables |

---

# PHASE B: Webhook Capture Feature

## B1. Create Webhook Models

**New file: `src/MailVoidApi/Models/WebhookBucket.cs`**
```csharp
[Table("WebhookBucket")]
[Index("IX_WebhookBucket_Name", nameof(Name), IsUnique = true)]
public class WebhookBucket
{
    [Key]
    public long Id { get; set; }
    [Required]
    public required string Name { get; set; }
    public string? Description { get; set; }
    [Required]
    public required Guid OwnerUserId { get; set; }
    public bool IsPublic { get; set; } = true;
    [Default(DefaultType.CurrentTimestamp)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastActivity { get; set; }
    public int? RetentionDays { get; set; } = 3;
}
```

**New file: `src/MailVoidApi/Models/Webhook.cs`**
```csharp
[Table("Webhook")]
[Index("IX_Webhook_BucketName", nameof(BucketName))]
[Index("IX_Webhook_CreatedOn", nameof(CreatedOn))]
public class Webhook
{
    [Key]
    public long Id { get; set; }
    [Required]
    public required string BucketName { get; set; }
    [Required]
    public required string HttpMethod { get; set; }
    [Required]
    public required string Path { get; set; }
    public string? QueryString { get; set; }
    [Required]
    public required string Headers { get; set; }  // JSON
    [Required]
    public required string Body { get; set; }
    public string? ContentType { get; set; }
    public string? SourceIp { get; set; }
    [Default(DefaultType.CurrentTimestamp)]
    public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
}
```

---

## B2. Update DatabaseService

Add to `InitializeAsync()`:
```csharp
await db.CreateTableIfNotExistsAsync<WebhookBucket>();
await db.CreateTableIfNotExistsAsync<Webhook>();
```

---

## B3. Create Backend Services

**New file: `src/MailVoidApi/Services/WebhookBucketService.cs`**
- `GetOrCreateBucket(name)` - auto-creates with admin owner
- `HasUserAccess(bucketId, userId)` - access check

**New file: `src/MailVoidApi/Services/WebhookCleanupService.cs`**
- BackgroundService for retention cleanup (same pattern as MailCleanupService)

---

## B4. Create Backend Controllers

**New file: `src/MailVoidApi/Controllers/HooksController.cs`**
- Route: `/hooks/{bucket}` and `/hooks/{bucket}/{**path}`
- Methods: POST, PUT, PATCH (no auth)
- Captures request and broadcasts via SignalR

**New file: `src/MailVoidApi/Controllers/WebhookManagementController.cs`**
- Route: `/api/webhooks` (requires auth)
- CRUD endpoints for buckets and webhooks

---

## B5. Update SignalR Service

**File: `src/MailVoidWeb/src/app/services/signalr.service.ts`**
- Add `WebhookNotification` interface
- Add `newWebhook$` observable
- Handle `NewWebhook` event

---

## B6. Create Frontend Service

**New file: `src/MailVoidWeb/src/app/_services/api/webhook.service.ts`**
- `getBuckets()`, `getWebhooks()`, `getWebhookDetail()`
- TypeScript interfaces for Webhook, WebhookBucket

---

## B7. Create Frontend Pages

**New files:**
- `src/MailVoidWeb/src/app/Pages/hooks/hooks.component.ts`
- `src/MailVoidWeb/src/app/Pages/hooks/hooks.component.html`
- `src/MailVoidWeb/src/app/Pages/hooks/hooks.component.scss`
- `src/MailVoidWeb/src/app/Pages/hook-detail/hook-detail.component.ts`
- `src/MailVoidWeb/src/app/Pages/hook-detail/hook-detail.component.html`
- `src/MailVoidWeb/src/app/Pages/hook-detail/hook-detail.component.scss`

---

## B8. Update Frontend Config

**`src/MailVoidWeb/src/app/app.routes.ts`**
- Add `/hooks` and `/hooks/:bucket/:id` routes

**`src/MailVoidWeb/src/app/app.config.ts`**
- Add Lucide icons: `Clipboard`, `Folder`

**`src/MailVoidWeb/src/app/Pages/main-nav-bar/main-nav-bar.component.ts`**
- Add "Hooks" navigation link

---

## B9. Files Summary - Phase B

### New Files (12)
| File | Purpose |
|------|---------|
| `src/MailVoidApi/Models/Webhook.cs` | Webhook entity |
| `src/MailVoidApi/Models/WebhookBucket.cs` | Bucket entity |
| `src/MailVoidApi/Services/WebhookBucketService.cs` | Bucket service |
| `src/MailVoidApi/Services/WebhookCleanupService.cs` | Cleanup service |
| `src/MailVoidApi/Controllers/HooksController.cs` | Public capture endpoint |
| `src/MailVoidApi/Controllers/WebhookManagementController.cs` | Management API |
| `src/MailVoidWeb/src/app/_services/api/webhook.service.ts` | Frontend service |
| `src/MailVoidWeb/src/app/Pages/hooks/hooks.component.*` | Hooks list page (3 files) |
| `src/MailVoidWeb/src/app/Pages/hook-detail/hook-detail.component.*` | Detail page (3 files) |

### Modified Files (5)
| File | Changes |
|------|---------|
| `src/MailVoidApi/Data/DatabaseService.cs` | Add webhook table creation |
| `src/MailVoidApi/Program.cs` | Register webhook services |
| `src/MailVoidWeb/src/app/app.routes.ts` | Add hooks routes |
| `src/MailVoidWeb/src/app/app.config.ts` | Add Lucide icons |
| `src/MailVoidWeb/src/app/services/signalr.service.ts` | Add webhook notifications |
| `src/MailVoidWeb/src/app/Pages/main-nav-bar/main-nav-bar.component.ts` | Add nav link |

---

# Implementation Order

1. **Phase A** (EF → OrmLite migration)
   - A1: Add submodule, update csproj
   - A2: Update all model attributes
   - A3: Create DatabaseService
   - A4: Update Program.cs
   - A5-A6: Update controllers and services
   - A7: Delete EF files
   - Test: Verify app runs with fresh DB

2. **Phase B** (Webhook feature)
   - B1-B2: Create webhook models, update DatabaseService
   - B3-B4: Create backend services and controllers
   - B5-B8: Create frontend components and configuration
   - Test: Verify webhook capture and viewing works

---

*This document will be updated as implementation progresses. Check boxes at the top track completion status.*
