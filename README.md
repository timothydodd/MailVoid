# MailVoid

A developer email testing tool that captures and organizes test emails without creating separate email accounts. It integrates with your mail server via webhooks and provides a clean web interface for viewing and managing messages. It also includes a webhook capture feature for testing HTTP webhooks.

![image](https://github.com/user-attachments/assets/320b036f-b522-44d7-8be3-b23d3f610128)


## Features

- Receive and store emails via mail server webhooks
- Organize emails into groups with configurable retention policies
- Capture and inspect arbitrary HTTP webhooks
- Real-time notifications via SignalR
- JWT authentication with refresh token rotation
- Responsive Angular SPA with dark theme

## Requirements

- .NET 10 SDK
- Node.js 22+ and npm
- MySQL 8.0+

## Getting Started

```bash
git clone https://github.com/timothydodd/MailVoid.git
cd MailVoid
```

### Backend

```bash
cd src/MailVoidApi
```

Configure `appsettings.json` with your MySQL connection and JWT settings:

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

Start the API (database tables are created automatically):

```bash
dotnet run
```

### Frontend

```bash
cd src/MailVoidWeb
npm install
npm start
```

Update `src/environments/environment.ts` if your API is not running on the default port.

### Default Credentials

- Username: `admin`
- Password: `admin`

## Docker

```bash
docker build -f src/MailVoidApi/Dockerfile -t mailvoid .
docker run -p 5133:80 mailvoid
```

## Tech Stack

| Layer    | Technology                                    |
| -------- | --------------------------------------------- |
| Backend  | .NET 10, RoboDodd.OrmLite (Dapper), SignalR   |
| Frontend | Angular 19, RxJS, Lucide, Bootstrap            |
| Database | MySQL 8.0+                                     |

## Project Structure

```
src/
├── MailVoidApi/           # .NET backend API
│   ├── Controllers/       # REST endpoints
│   ├── Services/          # Business logic
│   ├── Models/            # Entity models
│   └── Data/              # Database service
├── MailVoidSmtpServer/    # SMTP server for local development
├── MailVoidWeb/           # Angular frontend
│   └── src/app/
│       ├── Pages/         # Page components
│       ├── _components/   # Shared components
│       └── _services/     # API and auth services
└── RoboDodd.OrmLite/     # ORM submodule
```

## License

This project is licensed under the MIT License.
