# Platform API

Backend for the church management platform: one API serving the **admin console**, the **public website**, the **mobile app** and **partner integrations**. It is built for a single church today and is multi-tenant, so it is ready for SaaS.

- **.NET 10 / ASP.NET Core** minimal APIs, organised as a **modular monolith** (8 business modules)
- **PostgreSQL** with EF Core 10: one schema per module, migrations per module
- **Redis** (optional) as the second-level cache behind `HybridCache`
- Multi-tenancy, RBAC, JWT + rotating refresh tokens, 2FA, API keys, audit trail, transactional outbox
- OpenAPI with Scalar UI, Serilog, OpenTelemetry, health checks, rate limiting

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design and [docs/DATA-MODEL.md](docs/DATA-MODEL.md) for the schema.

## Quick start

Prerequisites: .NET 10 SDK, and either Docker or a local PostgreSQL 16+.

```bash
# 1. Dependencies (Postgres, Redis, Mailpit for viewing emails at http://localhost:8025)
docker compose up -d db redis mailpit

# 2. Run the API. Development mode migrates the database and seeds a church + owner account.
dotnet run --project src/Host/Platform.Api
```

- API docs (Scalar): http://localhost:5080/docs
- OpenAPI document: http://localhost:5080/openapi/v1.json
- Health: `/health/live`, `/health/ready`

Development sign-in (from `appsettings.Development.json`): `admin@example.com` / `ChangeMe!2026`. This owner of the seeded **grace** tenant is also a platform administrator.

```bash
curl -s -X POST http://localhost:5080/api/v1/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@example.com","password":"ChangeMe!2026","clientType":"Admin"}'
```

Public website/app endpoints are anonymous and identify the church with a header: `X-Tenant: grace`. A verified custom domain works too.

## Solution layout

```
src/
  BuildingBlocks/
    Platform.SharedKernel     Entity, AggregateRoot, domain events, Result/Error, Money, Address, Slug
    Platform.Application      Handler abstractions, permission catalogue, tenancy & user abstractions, paging
    Platform.Infrastructure   ModuleDbContext base, save interceptor (tenant/audit/soft delete/outbox),
                              outbox processor, caching, storage, email, number sequences
    Platform.Web              Endpoint helpers, validation filter, permission authorisation,
                              tenant resolution, problem-details errors
  Modules/<Name>/
    Platform.Modules.<Name>             Domain/  Features/  Infrastructure/  <Name>Module.cs
    Platform.Modules.<Name>.Contracts   Integration events + read-only directory interfaces
  Host/Platform.Api            Composition root (Program.cs, Modules.cs), seeding, configuration
tests/
  Platform.UnitTests           Domain rules (recurrence/DST, money, giving invariants, roles, slugs…)
  Platform.ArchitectureTests   Module boundaries enforced in CI
  Platform.IntegrationTests    Real API + real PostgreSQL: tenant isolation, token theft, concurrency
```

| Module | Schema | What it covers |
|---|---|---|
| Tenancy | `tenancy` | Organisations, branches/campuses, custom domains, settings, SaaS onboarding |
| Identity | `identity`, `audit` | Users, memberships, roles & permissions, sessions, 2FA, API keys, audit trail |
| People | `people` | Members & visitors, households, membership lifecycle, pastoral notes, follow-ups, custom fields |
| Groups | `groups` | Ministries, departments, cell groups, choirs (nestable) and rosters |
| Events | `events` | Services & events, recurrence, registrations/tickets/waitlist, check-in, head counts, reports |
| Giving | `giving` | Funds, split donations, receipts, counting batches, campaigns & pledges, online giving, statements |
| Content | `content` | Website pages (block-based), posts, sermons & series, media library, menus, scheduled publishing |
| Communications | `comms` | Announcements, prayer requests & wall, email/SMS/push/in-app broadcasts, devices, notifications |

## Common tasks

```bash
dotnet build Platform.slnx                       # warnings are errors
dotnet test --solution Platform.slnx             # needs Postgres; see below
dotnet tool restore                              # installs dotnet-ef (pinned in dotnet-tools.json)

# Add a migration after changing a module's model (example: People)
dotnet ef migrations add AddBloodGroup \
  -p src/Modules/People/Platform.Modules.People -s src/Host/Platform.Api -o Infrastructure/Migrations
```

Integration tests create and drop a uniquely named database. Point them at a server with `TEST_DB_CONNECTION` (default `Host=localhost;Port=5432;Username=postgres;Password=postgres`).

## Configuration

Every setting can be overridden with environment variables (`Section__Key`).

| Setting | Purpose |
|---|---|
| `ConnectionStrings__Database` | PostgreSQL connection (required) |
| `ConnectionStrings__Redis` | Optional distributed cache (recommended once you run more than one instance) |
| `Auth__SigningKey` | JWT signing key, ≥ 32 chars. **Keep it in a secret store, never in the repo.** |
| `Auth__Issuer`, `Auth__Audience`, `Auth__AppBaseUrl` | Token metadata; base URL used in email links |
| `Cors__AllowedOrigins__0..n` | Admin console / website origins |
| `Email__Provider` = `Smtp` + `Email__Smtp*` | Outgoing email (default `Log` writes emails to the log) |
| `Payments__DefaultProvider` = `Paystack`, `Payments__PaystackSecretKey` | Online giving (default `Sandbox`) |
| `Storage__*` | Media storage (local disk by default) |
| `Database__MigrateOnStartup` | Dev convenience; production uses migration bundles |
| `Seed__*` | Development seeding of the first tenant and owner |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Enables OpenTelemetry export (traces + metrics) |

## Production deployment

1. Build the image: `docker build -t platform-api .`
2. Run the migration bundles shipped in the image, **Identity first** (it owns the shared audit table):
   `/migrations/migrate-identity --connection "$DB"`, then tenancy, people, groups, events, giving, content, communications.
3. Start the container with `Database__MigrateOnStartup=false` and `Seed__Enabled=false`. Point probes at `/health/live` and `/health/ready`.
4. Put it behind TLS (a reverse proxy or load balancer). Forwarded headers are honoured.
