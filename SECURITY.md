# Security Policy

MailVoid receives mail and HTTP webhooks from the outside world and stores
them for developers to inspect, so bugs in it can leak test data or expose the
host it runs on. Reports are welcome and taken seriously.

## Supported versions

Only the latest tagged release (`vX.Y.Z`) and the current `main` container
images (`ghcr.io/timothydodd/mailvoid`, `ghcr.io/timothydodd/mailvoid-smtp`)
receive security fixes. Please upgrade before reporting.

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report privately via GitHub's
[private vulnerability reporting](https://github.com/timothydodd/MailVoid/security/advisories/new)
on this repository. Include:

- A description of the issue and its impact (what an attacker gains).
- Steps, a request sequence, or a message that reproduces it.
- The image tag or commit you tested against.

You should get an acknowledgement within 7 days. Confirmed issues are fixed in
a new release and disclosed via a GitHub security advisory once a fix is
available. Credit is given to reporters unless they prefer otherwise.

## Scope

In scope:

- Authentication or session flaws: JWT validation, refresh-token rotation,
  password hashing, personal API keys (`/api/v1`), or the mail-server API key
  on `/api/webhook/mail`.
- Authorization bypass: a user reading mail, mail groups, or webhook buckets
  they should not see; non-admins reaching admin or user-management endpoints;
  the read-only `/api/v1` surface modifying data.
- The SMTP server acting as an open relay, or accepting mail for addresses the
  mailbox filter should reject.
- Injection via stored content: HTML/JS from a received email or captured
  webhook executing in the web UI, or header/SQL injection in the Go services.
- Resource exhaustion a single unauthenticated client can trigger with modest
  traffic, beyond the documented SMTP and request-body limits.
- Unsafe handling of the SQLite database, spool, or certificate files.

Out of scope, by design:

- Default `admin` / `admin` credentials that were never changed.
- Deployments that expose the API or SMTP ports without the intended reverse
  proxy, or that list untrusted proxies in `ForwardedHeaders`.
- Content of test emails themselves. MailVoid is a testing tool and is not
  meant to hold production mail.
- Vulnerabilities in Angular, Go, or third-party modules; report those to the
  respective projects. Dependabot tracks them here and updates are applied in
  regular releases.
