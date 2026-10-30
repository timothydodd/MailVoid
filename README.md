# MailVoid

A developer email testing tool that captures and organizes test emails without creating separate email accounts. It integrates with your mail server via webhooks and provides a clean web interface for viewing and managing messages. It also includes a webhook capture feature for testing HTTP webhooks.

![image](https://github.com/user-attachments/assets/320b036f-b522-44d7-8be3-b23d3f610128)


## Features

- Receive and store emails via mail server webhooks
- Organize emails into groups with configurable retention policies
- Capture and inspect arbitrary HTTP webhooks
- Read-only `/api/v1` API with per-user API keys for test automation
- Real-time notifications via SignalR
- JWT authentication with refresh token rotation
- Responsive Angular SPA with dark theme

## Requirements

- Go 1.27+
- Node.js 22+ and npm
- SQLite (embedded; no separate install)

## Getting Started

```bash
git clone https://github.com/timothydodd/MailVoid.git
cd MailVoid
```

### Backend

The API and SMTP server are Go services in `src/go`. They read the same `appsettings.json`,
`appsettings.{Environment}.json`, user secrets (Development) and environment variables
(`Section__Key`) as the original .NET services, and use the existing SQLite database as-is.

Configure `src/go/cmd/mailvoid-api/appsettings.json` (or environment variables) with your
database and JWT settings:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=mailvoid.db"
  },
  "JwtSettings": {
    "Secret": "at-least-32-characters-long-secret-key",
    "Issuer": "mailvoid",
    "Audience": "mailvoid",
    "ExpiryMinutes": 30
  }
}
```

**Behind a proxy or not?** The client address used for login rate limiting, captured
webhook source IPs and logs comes from `X-Forwarded-For` only when the direct peer is a
trusted proxy. By default every loopback and private address is trusted, which suits an
in-cluster ingress such as Traefik. If clients can reach the API directly through NAT
(for example `docker run -p`, a Kubernetes NodePort, or a home router), their connections
also arrive from a private address and could spoof the header. In that case list your real
proxies explicitly, or a non-matching address to trust none:

```json
{
  "ForwardedHeaders": {
    "KnownProxies": [ "10.0.0.5" ],
    "KnownNetworks": [ "10.42.0.0/16" ]
  }
}
```

**SQLite journal mode.** The API opens the database in WAL mode so the UI can read while
mail is being written. If the database file is on a network filesystem (NFS/SMB), set
`Database:JournalMode` to `DELETE`.

Start the API (missing tables are created automatically; it listens on http://localhost:5133
unless `ASPNETCORE_URLS` / `ASPNETCORE_HTTP_PORTS` say otherwise):

```bash
cd src/go/cmd/mailvoid-api
go run .
```

The SMTP server runs the same way from `src/go/cmd/mailvoid-smtp`.

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

### Read-only API for tests and scripts

Each user can generate personal API keys under **User Settings → Account → API Keys**.
A key returns exactly what that user sees in the web app (same mail visibility and
webhook bucket rules) and can only read. Send it as `X-Api-Key: <key>` or
`Authorization: Bearer <key>`:

```bash
KEY=mv_...
curl -H "X-Api-Key: $KEY" "http://localhost:5133/api/v1/mail?to=someone@example.test&pageSize=10"
curl -H "X-Api-Key: $KEY"  http://localhost:5133/api/v1/mail/{id}
curl -H "X-Api-Key: $KEY"  http://localhost:5133/api/v1/mail/boxes
curl -H "X-Api-Key: $KEY"  http://localhost:5133/api/v1/webhooks/buckets
curl -H "X-Api-Key: $KEY"  http://localhost:5133/api/v1/webhooks/buckets/{bucket}/webhooks?page=1&pageSize=50
curl -H "X-Api-Key: $KEY"  http://localhost:5133/api/v1/webhooks/{id}
```

`GET /api/v1/mail` accepts `to`, `search`, `page`, `pageSize` (max 200), `sortBy`
(`createdOn`, `from`, `to`, `subject`) and `sortDirection`, and returns `{ items, totalCount }`.
Webhook list bodies are trimmed to 4 KB; the single-webhook endpoint returns the full body.
Only a hash of each key is stored, so the plaintext is shown once when it is created.

## Docker

```bash
docker build -f src/go/Dockerfile.api -t mailvoid src
docker run -p 5133:8080 mailvoid

docker build -f src/go/Dockerfile.smtp -t mailvoid-smtp src/go
```

## Tech Stack

| Layer    | Technology                                    |
| -------- | --------------------------------------------- |
| Backend  | Go (net/http, go-smtp), SignalR-compatible hub |
| Frontend | Angular 19, RxJS, Lucide, Bootstrap            |
| Database | SQLite                                         |

## Project Structure

```
src/
├── go/                    # Go backend
│   ├── cmd/mailvoid-api/  # API entrypoint (+ default appsettings)
│   ├── cmd/mailvoid-smtp/ # SMTP server entrypoint (+ default appsettings)
│   └── internal/          # api, smtpd, config (.NET-compatible), ordered
├── MailVoidWeb/           # Angular frontend
│   └── src/app/
│       ├── Pages/         # Page components
│       ├── _components/   # Shared components
│       └── _services/     # API and auth services
└── legacy/                # Original .NET services (kept until the Go port is signed off)
```

## License

This project is licensed under the MIT License.
