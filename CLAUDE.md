# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MailVoid is a developer email testing tool with three components:
- **Backend API**: Go service (`src/go/cmd/mailvoid-api`) that manages emails, authentication, and webhook integration
- **SMTP server**: Go service (`src/go/cmd/mailvoid-smtp`) that receives mail and forwards it to the API webhook
- **Frontend**: Angular 19 web application for viewing and managing test emails and webhooks

The original .NET 10 services live in `src/legacy/` and are kept only as a fallback until the Go port is signed off. Do not add features there. The Go services must stay drop-in compatible with them: same config keys, same SQLite schema and value formats, same HTTP/JSON contract.

The application receives webhook events from your mail server and stores emails in a SQLite database for developers to view through the web interface. It also includes a webhook capture feature for testing HTTP webhooks.

## Architecture

### Backend (src/go/)
- **internal/config**: .NET-compatible configuration (appsettings.json → appsettings.{Env}.json → user secrets in Development → env vars with `__` → command line; case-insensitive `Section:Key` lookups)
- **internal/api**: HTTP handlers (`handlers_*.go`), auth (Identity-compatible password hashes, JWT), SQLite access (`db.go`, `models.go`, `types.go`), mail extraction, retention cleanup, and a SignalR-protocol hub (`signalr.go`)
- **internal/smtpd**: go-smtp listeners, mailbox filter (anti open-relay), MIME parsing (enmime), durable disk spool, webhook forwarder, certificates / Let's Encrypt (certbot)
- **internal/ordered**: insertion-ordered string map used for mail headers
- **Authentication**: JWT access tokens with httpOnly refresh-token cookie rotation
- **Database**: SQLite only (pure-Go `modernc.org/sqlite`, no CGO). Only the current schema is created (`CREATE TABLE IF NOT EXISTS`); there are no migrations
- **Real-time**: SignalR hub at `/hubs/mail` (negotiate + WebSockets + JSON protocol) for the Angular `@microsoft/signalr` client

### Frontend (src/MailVoidWeb/)
- **Architecture**: Angular 19 standalone components with reactive forms and routing
- **Authentication**: JWT token-based auth with automatic refresh via HTTP interceptor
- **Services**: HttpClient-based API services with auth guards and interceptors
- **Components**: Organized into Pages/ and _components/ directories
- **Styling**: SCSS with Bootstrap-based custom styling system
- **Real-time**: SignalR client for receiving live email notifications
- **State Management**: BehaviorSubject patterns for reactive state management

## Development Commands

### Backend (Go)
```bash
# From src/go/
go build ./...                # Build everything
go vet ./...                  # Static checks
go test ./...                 # Run tests
cd cmd/mailvoid-api && go run .    # Run the API (http://localhost:5133 by default)
cd cmd/mailvoid-smtp && go run .   # Run the SMTP server
# Missing tables are created on startup; there are no migrations.
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
- **Database**: SQLite connection string in `ConnectionStrings:DefaultConnection` (e.g. `Data Source=mailvoid.db`). MySQL is not supported by the Go services
- **Listen address**: `ASPNETCORE_URLS` / `urls`, `Kestrel:Endpoints:*:Url`, or `ASPNETCORE_HTTP_PORTS` (the Docker image sets 8080); default `http://localhost:5133`
- **CORS**: Configure allowed origins via "CorsOrigins" setting
- **Trusted proxies**: `ForwardedHeaders:KnownNetworks` (CIDRs) / `ForwardedHeaders:KnownProxies` (IPs) decide whose `X-Forwarded-For` is believed for rate limiting, webhook `SourceIp` and logs. Default: loopback + private ranges (covers Traefik on k3s)
- **SMTP limits**: `SmtpServer:MaxRecipients` (50), `SmtpServer:MaxConnections` (100), `SmtpServer:MaxConnectionsPerIp` (10)
- **Request limits (API)**: bodies capped at 30 MB (64 KB for `/api/auth/*`, 128 MB for `/api/webhook/mail`, 5 MB for `/api/hook/*`), measured after request decompression

### Frontend Configuration
- **environment.ts**: Set API URL (default: http://localhost:5133)
- **environment.prod.ts**: Production API configuration

## Key Patterns

### Backend Patterns
- Plain `database/sql` with hand-written SQL; `queryOne` / `queryAll` / `scalarInt` helpers in `models.go`. Named parameters (`@UserId` + `sql.Named`) for shared predicates like `mailVisibilityClause`
- **Storage formats must match what .NET wrote**: GUIDs are upper-case TEXT in the DB (`GUID` type) but lower-case in JSON/JWT; datetimes are TEXT `yyyy-MM-dd HH:mm:ss.FFFFFFF` UTC (`DBTime` / `NullTime`) and JSON `yyyy-MM-ddTHH:mm:ss.fffZ`; bools are INTEGER 0/1; `Role` is stored as its integer and serialized as `"Admin"`/`"User"`
- JSON responses are camelCase with nulls included (pointer fields), matching ASP.NET Core output
- Routing uses Go 1.22+ `http.ServeMux` patterns in `server.go`; `requireAuth` = `[Authorize]`, `requireAPIKey` = `[ApiKey]`
- Pagination via `pagedResults[T]` (`items`, `totalCount`)

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

### Component Style Budgets (avoid SCSS bloat)
The prod build enforces per-component style budgets in `angular.json` (`anyComponentStyle`): **2 kB = warning, 4 kB = error**. Exceeding 4 kB **fails** `npm run build`. Do not raise these budgets to make a build pass — keep component stylesheets small instead. The budget measures the compiled/minified CSS, which is ≈ `sass --style=compressed` output (the WSL `esbuild` binary is win32-only, so the full build only runs on Windows). Keep each component comfortably under 4 kB by:
- **Reuse global styles** in `src/styles/` (imported via `src/styles.scss`) instead of redefining per component. Existing shared utilities: `.badge`/`.badge-success|danger|secondary` (`_utils.scss`), `.btn-icon` and `.copy-btn` (`_button.scss`), cards, forms, modal, tables, alerts. If a style is reusable, add it to the relevant global partial rather than a component.
- **Split large components into subcomponents** so their styles live in separate files, each under budget (e.g. `mail-detail` extracted its attachments panel into `mail-attachments/`).
- **Delete dead CSS** — selectors with no matching template usage (grep the template before assuming a class is used).

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
- **UserApiKey**: Per-user API keys (hashed) for the read-only `/api/v1` API
- **UserWebhookAccess**: Non-admin users allowed to use webhook capture (`Principal.CanUseWebhooks`, `requireWebhooks`; toggled via `PUT /api/UserManagement/{id}/webhooks`)

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
- `/api/webhook/mail`: Mail server webhook endpoint (API key: `X-Api-Key`, `api_key` query, or `Authorization: ApiKey <key>`)
- `/api/apikeys`: Personal API key management (requires auth; keys stored as SHA-256 in `UserApiKey`)
- `/api/v1/*`: Read-only mail + webhook endpoints for scripts, authenticated with a personal API key (`X-Api-Key` or `Authorization: Bearer mv_...`); `requireUserAPIKey` maps the key to its owner and reuses the normal handlers
- `/hubs/mail`: SignalR hub for real-time notifications

## Dependencies

### Backend Go Modules
- modernc.org/sqlite - pure-Go SQLite driver
- github.com/golang-jwt/jwt/v5 - JWT
- github.com/gorilla/websocket - SignalR WebSocket transport
- github.com/emersion/go-smtp - SMTP server
- github.com/jhillyerd/enmime - MIME parsing
- software.sslmate.com/src/go-pkcs12 - legacy .pfx certificates

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
