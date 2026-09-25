# Architecture

This document explains how the backend is put together and why. Read it before adding a module or changing a cross-cutting rule.

## 1. Goals

1. **One platform, many clients.** The admin console, public website, mobile app and partner integrations all use the same versioned API (`/api/v1`).
2. **One church now, SaaS later.** Every table that holds church data is tenant-scoped from day one, so the second church is onboarding, not a rewrite.
3. **Grow without a rewrite.** Modules are isolated so that any one of them can later be extracted into its own service.
4. **Trustworthy with sensitive data.** Pastoral notes, giving records and personal details are protected by tenant isolation, fine-grained permissions and a tamper-evident audit trail.

## 2. Modular monolith

```
                ┌───────────── Clients ─────────────┐
                Admin console   Website   Mobile app   Partners (API keys)
                        └───────────┬───────────┘
                          Platform.Api (one process)
     ┌──────────┬──────────┬────────┬────────┬────────┬─────────┬──────────┬──────────────┐
     │ Tenancy  │ Identity │ People │ Groups │ Events │ Giving  │ Content  │ Communications│
     └──────────┴──────────┴────────┴────────┴────────┴─────────┴──────────┴──────────────┘
          │  each module: own schema · own DbContext · own migrations · own outbox
     PostgreSQL ── Redis (cache) ── Object storage ── Email/SMS/Push/Payment providers
```

**Rules (enforced by `Platform.ArchitectureTests`):**

- A module never references another module's implementation. It may reference another module's `*.Contracts` assembly, which contains only integration events and read-only directory interfaces (`ITenantDirectory`, `IPeopleDirectory`, `IGroupDirectory`).
- There are no cross-schema foreign keys. Modules reference each other's records by id only (`PersonId`, `BranchId`, `OccurrenceId`…).
- `Domain/` code has no dependency on EF Core, ASP.NET or the module's `Infrastructure/`/`Features/`.
- `*.Contracts` depend only on `Platform.SharedKernel`.

**Why not microservices now?** Distributed systems are expensive: network failures, distributed transactions, and operating many deployments. A modular monolith keeps those boundaries without paying that cost. When a module needs independent scaling (notifications, once the mobile app has many users), you move its project into a new host, swap the in-process `IEventDispatcher` for a message broker, and replace its directory interfaces with HTTP clients. Its domain and features do not change.

### Inside a module (vertical slices)

```
Platform.Modules.Giving/
  Domain/            Aggregates and invariants (Donation, Fund, DonationBatch…). No framework code.
  Features/          One file per capability: request/response records, validator, endpoint handlers
  Infrastructure/    GivingDbContext, EF configurations, migrations, background jobs, adapters
  Payments/          Provider adapters behind IPaymentGateway
  GivingModule.cs    Registers services and maps endpoints (the module's only entry point)
```

Endpoints are thin. Business rules live in the domain (for example, `Donation.SetAllocations` enforces "allocations add up to the total, each fund at most once, amounts positive"). Expected failures return `Result`/`Error`, which map to RFC 9457 problem details. Invariant violations throw `DomainException`, which maps to HTTP 422.

## 3. Multi-tenancy

- **Model:** a shared database with a shared schema, and a `tenant_id` on every tenant-owned row (`ITenantOwned`). The tenant registry (`tenancy.tenants`) and login identities (`identity.users`) are global.
- **Resolution** (`TenantResolutionMiddleware`), in priority order:
  1. the `tid` claim of an authenticated principal (it cannot be overridden by headers)
  2. the `X-Tenant` header (slug or id), used by the website and mobile app
  3. the request host (a verified custom domain)
- **Isolation:** EF Core 10 **named query filters** (`Tenant`, `SoftDelete`) are applied automatically to every tenant-owned entity. A context without a tenant matches no rows. Background jobs and webhooks bind the tenant explicitly before they write.
- **Write protection:** the save interceptor stamps `tenant_id` on insert and **throws on any cross-tenant write**. This was proven by the integration tests, which caught a real bug during development.
- **Users across tenants:** one `User` can hold `TenantMembership`s in several churches, each with its own roles. `POST /auth/switch-tenant` issues tokens for another membership.
- **Hardening path:** add PostgreSQL row-level security as defence in depth, and move very large tenants to dedicated databases if ever needed. The `ITenantContext` seam makes both possible without changing application code.

## 4. Security

| Concern | Implementation |
|---|---|
| Passwords | ASP.NET Core `PasswordHasher` (PBKDF2-HMAC-SHA512, transparent re-hash), lockout after 5 failures, constant-time path for unknown emails |
| Access tokens | JWT HS256, 15 min, minimal claims (`sub`, `tid`, `mid`, `sid`) |
| Refresh tokens | Opaque `{sessionId}.{secret}`. Only the SHA-256 hash is stored. **Rotated on every use**; replaying a rotated token revokes the session (theft detection). A 30 s grace window covers concurrent mobile refreshes. Browser clients also get an HttpOnly, Secure, SameSite=Strict cookie. |
| 2FA | TOTP (RFC 6238) with secrets encrypted by Data Protection, plus hashed one-time recovery codes |
| Email links | Stateless Data Protection tokens bound to the user's security stamp, so they are invalidated by password or 2FA changes |
| Authorisation | Permission catalogue (`Permissions.cs`, `module.resource.action`). Roles are per-tenant sets of permissions. Effective permissions are cached (HybridCache) and invalidated per tenant on any role change. Endpoints declare `.RequirePermission(...)`. |
| Integrations | API keys (`X-Api-Key: pk_{prefix}_{secret}`), hashed at rest, scoped to specific permissions, revocable, with expiry |
| Webhooks | HMAC signature verification, idempotent processing, and amount/currency checks against the stored donation |
| Abuse | Rate limits: 20/min per IP on `/auth`, 300/min per IP on public endpoints, 1200/min per user globally |
| Uploads | Allow-listed MIME types, 50 MB cap, server-generated storage keys (client file names are never used as paths) |
| Headers | `nosniff`, `DENY` framing, `no-referrer`, a restrictive CSP on API responses, HSTS outside development |
| Audit | Every insert, update and delete, including before/after values, is written to `audit.audit_entries` **in the same transaction**. Secrets are redacted with `[AuditIgnore]`. |
| Privacy | `ConsentToContact` gates all broadcasts. Private pastoral notes are visible only to their author. Prayer requests reach the public wall only with the requester's consent **and** staff approval. |

## 5. Events and consistency

- Aggregates raise **domain events**. The save interceptor serialises them into the module's `outbox_messages` table in the **same transaction** as the state change, so no event is ever lost or published for a rolled-back change.
- `OutboxProcessor<TContext>` claims batches with `FOR UPDATE SKIP LOCKED` (safe with many API instances), dispatches each event in its own scope bound to the event's tenant, and retries failures up to 10 times, recording the error.
- Delivery is at least once, so **handlers are idempotent** (they check before creating).
- Cross-module flows implemented today:

| Event (publisher) | Reaction (subscriber) |
|---|---|
| `TenantCreated` (Tenancy) | Identity provisions system roles and invites the owner; Giving creates the default funds |
| `MemberAccountCreated` (Identity) | People links the account to an existing person with the same email, or creates one |
| `PersonLinkedToAccount` (People) | Identity stores the person id on the membership |
| `AttendanceRecorded` with first visit (Events) | People opens a high-priority "first-time visitor" follow-up |
| `DonationCompleted` (Giving) | Communications emails a receipt and adds an in-app notification |

## 6. Data design conventions

- **Ids:** UUIDv7 (`Guid.CreateVersion7()`). They are time-ordered (index-friendly), generated client-side, and cannot be guessed.
- **Naming:** snake_case tables and columns. Enums are stored as text (readable in SQL, and safe to reorder).
- **Money:** `numeric(18,2)` plus an ISO-4217 `char(3)` currency (the `Money` value object). Floating point is never used for money.
- **Case-insensitive text:** `citext` for emails, slugs and codes.
- **Soft delete** for business aggregates (`is_deleted`, `deleted_at`, `deleted_by`). Unique indexes are partial (`WHERE is_deleted = false`).
- **Optimistic concurrency:** PostgreSQL `xmin` on every aggregate. Conflicts return HTTP 409.
- **Flexible content** (page blocks, menus, custom fields, settings) lives in `jsonb`, validated at the API boundary, so the page builder can evolve without migrations.
- **Human-readable numbers:** member numbers (`M-000123`) and yearly receipt numbers (`R2026-000123`) come from an atomic per-tenant `number_sequences` table.
- **Time:** all instants are stored as `timestamptz` (UTC). Recurring events expand in the event's IANA time zone, which is DST-correct (unit-tested).

## 7. API conventions

- `/api/v1/...` covers staff and admin endpoints (permission-protected). `/api/v1/me/...` is the signed-in user's own data (website and mobile). `/api/v1/public/...` is anonymous and CDN-cacheable (`Cache-Control` set). `/api/v1/platform/...` is for the SaaS operator only.
- Lists return `{ items, page, pageSize, totalCount, totalPages, hasNextPage }`.
- Errors are problem details with a stable machine code, for example `{"status":409,"code":"registration.duplicate",...}`. Validation errors add an `errors` map keyed by camelCase field.
- Enums travel as strings (`"Member"`, `"BankTransfer"`).
- Breaking changes go to `/api/v2`. `v1` keeps working for app versions still installed on phones.

## 8. Reliability and operations

- **Health:** `/health/live` (process) and `/health/ready` (PostgreSQL and Redis).
- **Observability:** Serilog structured logs enriched with tenant and user; OpenTelemetry traces and metrics (ASP.NET, HttpClient, Npgsql), exported over OTLP when configured.
- **Background jobs** (hosted services, safe on multiple instances): outbox processors, occurrence scheduler, scheduled content publisher, broadcast dispatcher.
- **Resilience:** Npgsql retry-on-failure execution strategy; standard resilience pipeline (retry, circuit breaker, timeout) on the payment provider HTTP client.

## 9. Roadmap (designed for, not yet built)

| Area | Next step |
|---|---|
| Mobile | Firebase Cloud Messaging adapter for `IPushSender`; offline sync using `updated_at` cursors |
| Messaging | Real SMS adapter (Termii / Twilio / Africa's Talking) for `ISmsSender`; tenant-branded email templates |
| Giving | Stripe adapter; recurring gifts; year-end tax statement PDFs; accounting export |
| Identity | OpenID Connect server (OpenIddict) for third-party apps; social sign-in; passkeys |
| Children's ministry | Secure check-in with guardian pickup codes and label printing (attendance already records guardians) |
| Volunteers | Teams, positions and rosters/rotas with availability and reminders |
| SaaS | Plans and subscriptions, per-tenant feature flags, usage limits, billing, tenant data export and deletion |
| Scale | Extract Communications first (highest fan-out); message broker; read replicas for reporting |
