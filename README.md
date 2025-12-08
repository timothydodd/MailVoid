# MailVoid

MailVoid is a developer-focused email testing tool that simplifies managing multiple test email addresses without creating separate email accounts. It integrates with your mail server via webhooks to capture and organize test emails in a clean web interface.

![image](https://github.com/user-attachments/assets/320b036f-b522-44d7-8be3-b23d3f610128)

> [!WARNING]  
> This project is currently in prototype status. Expect frequent changes and updates.

## 🚀 Features

- **Backend API**: RESTful API built with C# .NET 10
  - JWT-based authentication with refresh token rotation
  - Webhook integration for email capture from your mail server
  - Webhook capture feature for testing HTTP webhooks
  - Health check endpoints for monitoring
  - Real-time notifications via SignalR

- **Web Frontend**: Modern Angular 19 SPA
  - Clean, responsive interface for email management
  - Email grouping and organization
  - Webhook capture and inspection UI
  - User settings and password management
  - Real-time email and webhook notifications

- **Database**: MySQL with RoboDodd.OrmLite (Dapper-based)
  - Efficient email storage and retrieval
  - User management and authentication
  - Email grouping with retention policies
  - Webhook bucket organization

## 📋 Requirements

- .NET 10 SDK
- Node.js 22+ and npm
- MySQL 8.0+
- Mail server with webhook support (optional)

## 🛠️ Installation

### Clone the Repository

```bash
git clone https://github.com/timothydodd/MailVoid.git
cd MailVoid
```

### Backend Setup

1. Navigate to the API directory:
   ```bash
   cd src/MailVoidApi
   ```

2. Configure your settings in `appsettings.json`:
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=localhost;Database=mailvoid;User=root;Password=yourpassword;"
     },
     "JwtSettings": {
       "Secret": "your-256-bit-secret-key-here",
       "Issuer": "MailVoidApi",
       "Audience": "MailVoidClient",
       "ExpiryMinutes": 15
     }
   }
   ```

3. Start the API (tables are created automatically on startup):
   ```bash
   dotnet run
   ```

### Frontend Setup (Development)

1. Navigate to the web directory:
   ```bash
   cd src/MailVoidWeb
   ```

2. Install dependencies:
   ```bash
   npm install
   ```

3. Configure the API endpoint in `src/environments/environment.ts` if needed

4. Start the development server:
   ```bash
   npm start
   ```

## 🎯 Usage

1. Access the application at `http://localhost:8200` (development) or `http://localhost:5133` (production)
2. Log in with default credentials:
   - Username: `admin`
   - Password: `admin`
3. Configure your mail server to send webhooks to your API endpoint
4. Start receiving and managing test emails!

## 📝 Available Scripts

### Backend (.NET API)
```bash
dotnet run        # Run the API with frontend proxy
dotnet build      # Build the project
dotnet test       # Run tests
```

### Frontend (Angular)
```bash
npm start         # Start development server
npm run build     # Build for production
npm run lint      # Run ESLint
npm run format    # Format code with Prettier
npm test          # Run unit tests
```

## 🐳 Docker Support

Build and run with Docker:

```bash
docker build -f src/MailVoidApi/Dockerfile -t mailvoid .
docker run -p 5133:80 mailvoid
```

## 🧪 Development

### Technologies Used

**Backend:**
- .NET 10
- RoboDodd.OrmLite (Dapper-based micro ORM)
- JWT Authentication
- SignalR for real-time updates

**Frontend:**
- Angular 19 with standalone components
- RxJS for reactive programming
- Lucide icons
- Bootstrap-based custom styling
- ngx-toastr for notifications
- @ng-select for enhanced dropdowns

### Project Structure

```
MailVoid/
├── src/
│   ├── MailVoidApi/          # .NET Backend API
│   │   ├── Controllers/      # API endpoints
│   │   ├── Services/         # Business logic
│   │   ├── Models/           # Entity models
│   │   └── Data/             # Database service
│   └── MailVoidWeb/         # Angular Frontend
│       ├── src/app/
│       │   ├── Pages/       # Page components
│       │   ├── _components/ # Shared components
│       │   └── _services/   # Angular services
│       └── src/styles/      # Global styles
└── .github/workflows/       # CI/CD pipelines
```

## 🤝 Contributing

This project is in prototype phase. Contributions are welcome but please wait until we're closer to v1.0 for major changes. Feel free to:

1. Report bugs and issues
2. Suggest new features
3. Submit pull requests for fixes

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 🙏 Acknowledgments

- [Microsoft](https://microsoft.com/) for .NET and development tools
- [Angular](https://angular.io/) for the frontend framework
- [Dapper](https://github.com/DapperLib/Dapper) for the micro ORM foundation