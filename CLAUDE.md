# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

MailVoid is a developer email testing tool with two main components:
- **Backend API**: C# .NET 9 web API that manages emails, authentication, and webhook integration
- **Frontend**: Angular 19 web application for viewing and managing test emails

The application receives webhook events from SendGrid and stores emails in a MySQL database for developers to view through the web interface.

## Architecture

### Backend (src/MailVoidApi/)
- **Controllers**: REST API endpoints for authentication, mail management, and webhooks
- **Services**: Core business logic including AuthService, MailGroupService, UserService, and background task processing
- **Models**: Entity classes for User, Mail, MailGroup, and RefreshToken with Entity Framework annotations
- **Data**: MailVoidDbContext for Entity Framework Core database operations
- **Common**: Shared utilities including pagination, caching (TimedCache), and database extensions
- **Authentication**: JWT-based authentication with refresh token support
- **Database**: MySQL with Entity Framework Core and Pomelo MySQL provider

### Frontend (src/MailVoidWeb/)
- **Architecture**: Angular 19 standalone components with reactive forms and routing
- **Authentication**: JWT token-based auth with automatic refresh via HTTP interceptor
- **Services**: HttpClient-based API services with auth guards and interceptors
- **Components**: Organized into Pages/ and _components/ directories
- **Styling**: SCSS with Bootstrap-based custom styling system

## Development Commands

### Backend (.NET API)
```bash
# From src/MailVoidApi/
dotnet run                    # Run API (also starts frontend via SPA proxy)
dotnet build                  # Build the API
dotnet test                   # Run tests (if any exist)
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
- Controllers use Entity Framework Core with Pomelo MySQL provider for database operations
- JWT authentication with refresh token rotation
- Background task queue for async operations
- Pagination implemented via PagedResults<T> utility
- Custom exception handling and logging
- DbContext pattern with MailVoidDbContext for data access

### Frontend Patterns
- Standalone Angular components (no NgModules)
- Reactive forms with validation
- HTTP interceptors for auth token handling
- Route guards for authentication
- BehaviorSubject for state management in services
- Component-specific SCSS styling

## Authentication Flow
1. Login with username/password receives JWT access token + refresh token
2. HTTP interceptor automatically adds Bearer token to requests
3. Refresh token automatically rotates when access token expires
4. Logout revokes all user refresh tokens

## Database Schema
- **User**: Authentication and user data
- **Mail**: Email storage with grouping support
- **MailGroup**: Rules-based email organization
- **RefreshToken**: Secure token rotation for authentication

## Default Credentials
- Username: admin
- Password: admin

## API Endpoints
- `/api/auth/*`: Authentication endpoints
- `/api/mail/*`: Email management and retrieval
- `/api/health`: Health check endpoint
- External webhook endpoint for SendGrid integration