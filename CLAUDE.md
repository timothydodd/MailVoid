# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MailVoid is a developer email testing tool with two main components:
- **Backend API**: C# .NET 10 web API that manages emails, authentication, and webhook integration
- **Frontend**: Angular 19 web application for viewing and managing test emails and webhooks

The application receives webhook events from your mail server and stores emails in a MySQL database for developers to view through the web interface. It also includes a webhook capture feature for testing HTTP webhooks.

## Architecture

### Backend (src/MailVoidApi/)
- **Controllers**: REST API endpoints for authentication, mail management, webhook capture, and webhook management
- **Services**: Core business logic including AuthService, MailGroupService, UserService, WebhookBucketService, and background task processing
- **Models**: Entity classes for User, Mail, MailGroup, RefreshToken, Webhook, and WebhookBucket with RoboDodd.OrmLite annotations
- **Data**: DatabaseService for RoboDodd.OrmLite database operations (Dapper-based)
- **Common**: Shared utilities including pagination, caching (TimedCache), and database extensions
- **Authentication**: JWT-based authentication with refresh token support
- **Database**: MySQL with RoboDodd.OrmLite (Dapper-based micro ORM)
- **Real-time**: SignalR hub for real-time email and webhook notifications

### Frontend (src/MailVoidWeb/)
- **Architecture**: Angular 19 standalone components with reactive forms and routing
- **Authentication**: JWT token-based auth with automatic refresh via HTTP interceptor
- **Services**: HttpClient-based API services with auth guards and interceptors
- **Components**: Organized into Pages/ and _components/ directories
- **Styling**: SCSS with Bootstrap-based custom styling system
- **Real-time**: SignalR client for receiving live email notifications
- **State Management**: BehaviorSubject patterns for reactive state management

## Development Commands

### Backend (.NET API)
```bash
# From src/MailVoidApi/
dotnet run                    # Run API (also starts frontend via SPA proxy)
dotnet build                  # Build the API
dotnet test                   # Run tests (if any exist)
# Note: Database tables are created automatically on startup (no migrations needed)
```

### Frontend (Angular)
```bash
# From src/MailVoidWeb/
npm install                   # Install dependencies
npm start                     # Start dev server (ng serve)
npm run build                 # Build for production
npm run prod                  # Build with production config
npm run local                 # Serve with local configuration
npm test                      # Run unit tests via Karma
npm run lint                  # Run ESLint
npm run format                # Format code with Prettier
```

## Configuration

### Backend Configuration
- **appsettings.json**: Configure database connection, JWT settings, and CORS origins
- **JWT Settings**: Requires symmetric key, issuer, audience, and expiry configuration
- **Database**: MySQL connection string required in "DefaultConnection"
- **CORS**: Configure allowed origins via "CorsOrigins" setting

### Frontend Configuration
- **environment.ts**: Set API URL (default: http://localhost:5133)
- **environment.prod.ts**: Production API configuration

## Key Patterns

### Backend Patterns
- Controllers use RoboDodd.OrmLite (Dapper-based) for database operations
- Use `db.From<T>()` for fluent query building with `.Where()`, `.OrderBy()`, `.Limit()`
- Use `db.SelectAsync<T>()`, `db.SingleAsync<T>()`, `db.InsertAsync()`, `db.UpdateAsync()`, `db.DeleteAsync()`
- Use `db.CountAsync<T>()` for counting records
- JWT authentication with refresh token rotation
- Background task queue for async operations
- Pagination implemented via PagedResults<T> utility
- Custom exception handling and logging
- DatabaseService pattern for connection management

### Frontend Patterns
- Standalone Angular components (no NgModules)
- Reactive forms with validation
- HTTP interceptors for auth token handling
- Route guards for authentication
- BehaviorSubject for state management in services
- Component-specific SCSS styling
- **IMPORTANT**: When adding new Lucide icons to components, you MUST update `src/app/app.config.ts`:
  1. Add the icon to the import statement from 'lucide-angular'
  2. Add the icon to the `LucideAngularModule.pick({})` configuration
  3. Example: For Clock icon, add `Clock` to both the import and the pick object
- **IMPORTANT**: For Lucide icons, ALWAYS use the `size` attribute instead of setting `width` and `height` in CSS. Use `<lucide-icon name="icon-name" size="16"></lucide-icon>` format.

## Authentication Flow
1. Login with username/password receives JWT access token + refresh token
2. HTTP interceptor automatically adds Bearer token to requests
3. Refresh token automatically rotates when access token expires
4. Logout revokes all user refresh tokens

## Database Schema
- **User**: Authentication and user data
- **Mail**: Email storage with grouping support
- **MailGroup**: Rules-based email organization with retention policies
- **RefreshToken**: Secure token rotation for authentication
- **UserMailRead**: Tracking read status for emails per user
- **Webhook**: Captured HTTP webhook requests
- **WebhookBucket**: Organization for webhook captures with retention policies

## Default Credentials
- Username: admin
- Password: admin

## API Endpoints
- `/api/auth/*`: Authentication endpoints (login, logout, refresh)
- `/api/mail/*`: Email management and retrieval
- `/api/mailgroup/*`: Mail group management
- `/api/user/*`: User management and settings
- `/api/health`: Health check endpoint
- `/api/hook/{bucket}`: Webhook capture endpoint (POST/PUT/PATCH - no auth required)
- `/api/webhooks/*`: Webhook management API (requires auth)
- `/webhooks/mail`: Mail server webhook endpoint
- `/mailHub`: SignalR hub for real-time notifications

## Dependencies

### Backend NuGet Packages
- Microsoft.AspNetCore.Authentication.JwtBearer
- RoboDodd.OrmLite (git submodule - Dapper-based micro ORM)
- MySqlConnector
- Dapper
- Microsoft.AspNetCore.SpaProxy
- AspNetCore.HealthChecks.MySql
- Microsoft.Extensions.Caching.Memory

### Frontend NPM Packages
- Angular 19 (v20.1.5) - Core framework
- @auth0/angular-jwt - JWT token management
- @ng-select/ng-select - Enhanced select components
- @microsoft/signalr - Real-time communication
- lucide-angular - Icon library
- ngx-toastr - Toast notifications
- ngx-valdemort - Form validation

# important-instruction-reminders
Do what has been asked; nothing more, nothing less.
NEVER create files unless they're absolutely necessary for achieving your goal.
ALWAYS prefer editing an existing file to creating a new one.
NEVER proactively create documentation files (*.md) or README files. Only create documentation files if explicitly requested by the User.
