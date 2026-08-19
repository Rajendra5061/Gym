# Gym Management System

A complete gym management product: a **React single-page application** talking to an
**ASP.NET Core 8 Web API** backed by **SQL Server**.

```
frontend/ (React + TypeScript + Vite)
  │  HTTP only
  ▼
backend/src/GymManagement.Api  ──►  Infrastructure  ──►  Application  ──►  Domain
                                          │
                                          ▼
                                    SQL Server (GymDatabase)
```

## Quick start

```bash
# 1. API — applies migrations and seeds reference data on first run
dotnet run --project backend/src/GymManagement.Api        # https://localhost:7135  (Swagger at /swagger)

# 2. Frontend
cd frontend && npm install && npm run dev         # http://localhost:5173
```

Sign in with **admin** / **123@**.
See the note in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md#sign-in) — that password is a local
development convenience and does not meet the policy the API enforces on password changes.

## Documentation

| Document | Contents |
|---|---|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Layout, frontend structure, running it, database, security, API surface, known limitations |
| [docs/TestCases.md](docs/TestCases.md) | ~180 manual and automated test cases mapped to the requirements |

## Tests

```bash
dotnet test backend/GymManagement.sln   # backend unit + integration
cd frontend && npm run typecheck
```

## Features

Members · membership plans · subscriptions (renew, upgrade/downgrade, freeze/resume, cancel,
grace period, auto-expiry) · payments with receipts, refunds and a UPI workflow · attendance ·
trainers · exercises and workout plans · equipment · enquiries · feedback · expenses ·
14 report types with Excel and PDF export · notifications · users, roles and permissions ·
audit log · recycle bin · gym settings · trial and licensing · database backup and restore.
