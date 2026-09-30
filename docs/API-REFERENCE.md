# Platform API Reference

Welcome to the comprehensive API Reference document for the Platform backend API (church management SaaS, .NET 10). This document serves as the primary contract for frontend developers (Admin Portal) and public website consumers.

## Table of Contents
1. [Base URLs](#1-base-urls)
2. [Authentication](#2-authentication)
3. [Conventions](#3-conventions)
4. [Modules](#4-modules)
   - [Auth Group](#auth-group)
   - [My Account](#my-account)
   - [Administration](#administration)
   - [People](#people)
   - [Organisation](#organisation)
   - [Groups](#groups)
   - [Events](#events)
   - [Giving](#giving)
   - [Content](#content)
   - [Communications](#communications)
   - [Dashboard](#dashboard)
   - [Public Endpoints](#public-endpoints)
   - [Platform Admin](#platform-admin)
5. [Key Response Shapes](#5-key-response-shapes)
6. [Permissions Catalogue](#6-permissions-catalogue)
7. [Error Code Reference](#7-error-code-reference)

---

## 1. Base URLs

- **Local Development:** `http://localhost:5080/api/v1`
- **OpenAPI/Scalar UI:** `http://localhost:5080/docs`

---

## 2. Authentication

The API supports three forms of authentication depending on the endpoint context.

### Bearer JWT
For authenticated user sessions (Admin Portal, Member App). The Access Token has a 15-minute lifespan.
- **Header:** `Authorization: Bearer <access_token>`
- **Refresh:** `POST /auth/refresh` with `{ "refreshToken": "..." }`

```bash
curl -X GET http://localhost:5080/api/v1/auth/me \
  -H "Authorization: Bearer eyJhbGciOi..."
```

### API Keys
For server-to-server integrations or external automated clients.
- **Header:** `X-Api-Key: pk_{prefix}_{secret}`

```bash
curl -X GET http://localhost:5080/api/v1/members \
  -H "X-Api-Key: pk_test_1234567890abcdef"
```

### Public & Tenant Headers
For public endpoints, requests are anonymous but MUST specify the tenant context.
- **Header:** `X-Tenant: {slug}`

```bash
curl -X GET http://localhost:5080/api/v1/public/events \
  -H "X-Tenant: grace-church"
```

---

## 3. Conventions

### Pagination
List endpoints returning multiple records use a standard `PagedResult` envelope:
```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalCount": 150,
  "totalPages": 8,
  "hasNextPage": true
}
```
Query parameters `page` and `pageSize` are standard across all list endpoints.

### Errors
Errors follow the **RFC 9457 Problem Details** standard. A stable `code` field is included for programmable error handling.
```json
{
  "type": "https://api.platform.com/errors/validation",
  "title": "Validation Failed",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "code": "validation.failed",
  "errors": {
    "email": ["Email address is invalid."]
  }
}
```

### Data Formats
- **Timestamps:** All dates and times are in ISO 8601 UTC format (e.g., `2026-09-29T23:47:25Z`).
- **IDs:** All resource identifiers are UUID v7 strings.

### Sudo Endpoints
High-security mutations (e.g., MFA setup, changing user roles) require a fresh **sudo token**.
1. Call `POST /auth/reauthenticate` with the user's password.
2. Receive a `{ sudoToken }` valid for 5 minutes.
3. Pass it in the header: `X-Sudo-Token: {sudoToken}`.
Failure to provide a valid sudo token results in a `403 auth.sudo_required` error.

### Rate Limits
Auth endpoints are strictly rate-limited to **20 requests per minute** per IP/User to prevent brute-force attacks.

---

## 4. Modules

### Auth Group
Rate-limited to 20/min.

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| POST | `/auth/login` | None | None | **Req:** `{ email, password, clientType, tenantSlug?, remember? }`<br>**Res:** `{ status: "authenticated"\|"mfa_required", tokens?, mfaToken? }` |
| POST | `/auth/mfa/verify` | None | None | **Req:** `{ mfaToken, code, type: "totp"\|"recovery" }`<br>**Res:** `{ status, tokens }` |
| POST | `/auth/refresh` | None | None | **Req:** `{ refreshToken }`<br>**Res:** `{ accessToken, expiresIn, refreshToken, refreshExpiresIn }` |
| POST | `/auth/logout` | None | None | **Req:** `{ refreshToken }`<br>**Res:** `204 No Content` |
| POST | `/auth/signup` | None | None | **Req:** `{ firstName, lastName, email, password, phone?, roleId? }`<br>**Res:** `202 Accepted` |
| POST | `/auth/verify-email` | None | None | **Req:** `{ email, code }`<br>**Res:** `200 OK` |
| POST | `/auth/verify-email/resend`| None | None | **Req:** `{ email }`<br>**Res:** `200 OK` |
| POST | `/auth/password/forgot` | None | None | **Req:** `{ email }`<br>**Res:** `200 OK` |
| POST | `/auth/password/reset` | None | None | **Req:** `{ email, token, newPassword }`<br>**Res:** `204 No Content` |
| POST | `/auth/reauthenticate` | Bearer | None | **Req:** `{ password }`<br>**Res:** `{ sudoToken }` (5-min duration) |
| POST | `/auth/session/touch` | Bearer | None | Keep session alive.<br>**Res:** `204 No Content` |
| GET | `/auth/me` | Bearer | None | Profile context.<br>**Res:** `{ id, name, email, phone, avatarUrl, role, permissions:[], isPlatformAdmin, tenantId, membershipId }` |
| GET | `/auth/requestable-roles` | X-Tenant | None | **Res:** `{ roles: [{ id, name, description }] }` |
| GET | `/auth/invitations/{token}` | None | None | **Res:** `{ email, roleName, tenantName, expiresAt }` |
| POST | `/auth/invitations/{token}/accept`| None | None | **Req:** `{ firstName, lastName, password }`<br>**Res:** `{ tokens }` |
| POST | `/auth/switch-tenant` | Bearer | None | **Req:** `{ tenantId }`<br>**Res:** `{ status, tokens }` |

### My Account
Endpoints prefixed with `/me` to manage the authenticated user's profile and security.

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| GET | `/me/profile` | Bearer | None | **Res:** `{ name, email, phone, avatarUrl }` |
| PATCH | `/me/profile` | Bearer | None | **Req:** `{ name, phone? }`<br>**Res:** `204 No Content` |
| POST | `/me/password` | Bearer | None | **Req:** `{ currentPassword, newPassword }`<br>**Res:** `204 No Content` |
| GET | `/me/security` | Bearer | None | **Res:** `{ mfaEnabled, recoveryCodesRemaining, passwordChangedAt, sessions: [{ id, browser, os, ip, lastActiveAt, createdAt, current }] }` |
| POST | `/me/mfa/setup` | Sudo | None | **Res:** `{ secret, otpauthUrl }` |
| POST | `/me/mfa/enable` | Bearer | None | **Req:** `{ code }`<br>**Res:** `{ recoveryCodes: string[] }` |
| DELETE| `/me/mfa` | Sudo | None | **Res:** `204 No Content` |
| POST | `/me/mfa/recovery-codes` | Sudo | None | **Res:** `{ recoveryCodes: string[] }` |
| DELETE| `/me/sessions/{id}` | Bearer | None | Revoke specific session. `204 No Content` |
| POST | `/me/sessions/revoke-others`| Sudo | None | **Res:** `{ revoked: number }` |
| GET | `/me/notification-preferences`| Bearer | None | **Res:** `{ accessRequests, formResponses, campaignReports, weeklySummary }` |
| PUT | `/me/notification-preferences`| Bearer | None | **Req:** Same as GET.<br>**Res:** `204 No Content` |
| POST | `/me/avatar` | Bearer | None | **Req:** `multipart/form-data` (field: `file`, ≤5 MB, JPEG/PNG/WebP/GIF)<br>**Res:** `{ avatarUrl }` |
| DELETE| `/me/avatar` | Bearer | None | **Res:** `204 No Content` |
| GET | `/me/tenants` | Bearer | None | **Res:** `[{ tenantId, tenantName, tenantSlug, membershipId, roleName, status, isCurrent }]` |

### Administration
Manage users, access requests, roles, and API keys.

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| GET | `/admin/users` | Bearer | `users:manage` | **Query:** `?page&pageSize&search&status&roleId`<br>**Res:** PagedResult |
| POST | `/admin/users/invite` | Sudo | `users:manage` | **Req:** `{ email, roleId, unitId? }`<br>**Res:** `201 Created` |
| POST | `/admin/users/{id}/invite/resend`| Bearer | `users:manage` | **Res:** `204 No Content` |
| DELETE| `/admin/users/{id}/invite`| Bearer | `users:manage` | **Res:** `204 No Content` |
| POST | `/admin/users/{id}/role` | Sudo | `users:manage` | **Req:** `{ roleId }`<br>**Res:** `204 No Content` |
| POST | `/admin/users/{id}/suspend`| Sudo | `users:manage` | **Res:** `204 No Content` |
| POST | `/admin/users/{id}/reactivate`| Sudo | `users:manage` | **Res:** `204 No Content` |
| POST | `/admin/users/{id}/mfa/reset`| Sudo | `users:manage` | **Res:** `204 No Content` |
| GET | `/admin/access-requests`| Bearer | `users:manage` | **Query:** `?page&pageSize&status`<br>**Res:** PagedResult |
| POST | `/admin/access-requests/{id}/approve`| Sudo | `users:manage` | **Req:** `{ roleId }`<br>**Res:** `204 No Content` |
| POST | `/admin/access-requests/{id}/reject`| Bearer | `users:manage` | **Req:** `{ reason? }`<br>**Res:** `204 No Content` |
| GET | `/admin/roles` | Bearer | `roles:manage` | **Res:** `[{ id, name, description, isSystem, permissions: [] }]` |
| GET | `/admin/roles/{id}` | Bearer | `roles:manage` | **Res:** Role Detail |
| POST | `/admin/roles` | Bearer | `roles:manage` | **Req:** `{ name, description, permissions: [] }`<br>**Res:** `201 Created` |
| PUT | `/admin/roles/{id}` | Bearer | `roles:manage` | **Req:** `{ name, description, permissions: [] }`<br>**Res:** `204 No Content` |
| DELETE| `/admin/roles/{id}` | Bearer | `roles:manage` | **Res:** `204 No Content` |
| GET | `/admin/api-keys` | Bearer | `api_keys:manage` | **Res:** List of API Keys |
| POST | `/admin/api-keys` | Bearer | `api_keys:manage` | **Req:** `{ name, scopes: [], expiresAt? }`<br>**Res:** `{ key: "pk_...", ...metadata }` |
| DELETE| `/admin/api-keys/{id}` | Bearer | `api_keys:manage` | **Res:** `204 No Content` |
| GET | `/admin/audit` | Bearer | `audit:view` | **Query:** `?page&pageSize&from&to&action&userId`<br>**Res:** PagedResult |

### People
CRM endpoints for members and congregations.

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| GET | `/members` | Bearer | `members:view`\|`manage` | **Query:** `?page&pageSize&search&status&unitId&stageId&scope`<br>**Res:** PagedResult |
| GET | `/members/{id}` | Bearer | `members:view`\|`manage` | **Res:** Full member profile |
| POST | `/members` | Bearer | `members:manage` | **Req:** `{ firstName, lastName, email?, phone?, ... }`<br>**Res:** `201 Created` |
| PUT | `/members/{id}` | Bearer | `members:manage` | **Req:** Full member update<br>**Res:** `204 No Content` |
| DELETE| `/members/{id}` | Bearer | `members:manage` | Soft delete. `204 No Content` |
| POST | `/members/bulk/approve` | Bearer | `members:manage` | **Req:** `{ ids: [] }`<br>**Res:** `204 No Content` |
| POST | `/members/bulk/deactivate`| Bearer | `members:manage` | **Req:** `{ ids: [], reason? }`<br>**Res:** `204 No Content` |
| POST | `/members/bulk/delete` | Sudo | `members:manage` | **Req:** `{ ids: [] }`<br>**Res:** `204 No Content` |
| GET | `/members/export.csv` | Bearer | `members:export` | **Res:** CSV Download |
| POST | `/members/{id}/stage` | Bearer | `members:manage` | **Req:** `{ stageId, note? }`<br>**Res:** `204 No Content` |
| GET | `/members/{id}/stage-history`| Bearer | `members:view`\|`manage` | **Res:** List of stage transitions |
| GET | `/members/{id}/follow-ups` | Bearer | `pastoral:view`\|`manage` | **Res:** List of follow-ups |
| POST | `/members/{id}/follow-ups` | Bearer | `pastoral:manage` | **Req:** `{ type, note, dueDate? }`<br>**Res:** `201 Created` |
| PATCH | `/members/{id}/follow-ups/{fid}`| Bearer | `pastoral:manage` | **Req:** `{ status }`<br>**Res:** `204 No Content` |
| GET | `/members/custom-fields`| Bearer | `members:view`\|`manage` | **Res:** Custom fields schema |
| POST | `/members/custom-fields`| Bearer | `custom_fields:manage`| **Req:** `{ label, type, required }`<br>**Res:** `201 Created` |

### Organisation
Tenant-wide settings and branches.

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| GET | `/organisation` | Bearer | `settings:manage` | **Res:** `{ name, slug, kind, timeZone, currency, locale, logoUrl, address?, socialLinks? }` |
| PATCH | `/organisation` | Bearer | `settings:manage` | Partial update.<br>**Res:** `204 No Content` |
| GET | `/organisation/settings`| Bearer | `settings:manage` | **Res:** Key-value map |
| PUT | `/organisation/settings/{key}`| Bearer | `settings:manage` | **Req:** `{ value }`<br>**Res:** `204 No Content` |
| GET | `/organisation/branches`| Bearer | `settings:manage` | **Res:** List of branches |
| POST | `/organisation/branches`| Bearer | `settings:manage` | **Res:** `201 Created` |
| PUT | `/organisation/branches/{id}`| Bearer| `settings:manage` | **Res:** `204 No Content` |
| DELETE| `/organisation/branches/{id}`| Bearer| `settings:manage` | **Res:** `204 No Content` |

### Groups

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| GET | `/groups` | Bearer | `groups:view`\|`manage` | **Query:** `?page&pageSize&search&parentId&kind`<br>**Res:** PagedResult |
| GET | `/groups/{id}` | Bearer | `groups:view`\|`manage` | **Res:** Group detail + member count |
| POST | `/groups` | Bearer | `groups:manage` | **Res:** `201 Created` |
| PUT | `/groups/{id}` | Bearer | `groups:manage` | **Res:** `204 No Content` |
| DELETE| `/groups/{id}` | Bearer | `groups:manage` | **Res:** `204 No Content` |
| GET | `/groups/{id}/members` | Bearer | `groups:view`\|`manage` | **Res:** PagedResult |
| POST | `/groups/{id}/members` | Bearer | `groups:manage` | **Req:** `{ personId }`<br>**Res:** `201 Created` |
| DELETE| `/groups/{id}/members/{memberId}`| Bearer| `groups:manage`| **Res:** `204 No Content` |

### Events

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| GET | `/events` | Bearer | `events:view`\|`manage` | **Query:** `?page&pageSize&search&from&to&status`<br>**Res:** PagedResult |
| GET | `/events/{id}` | Bearer | `events:view`\|`manage` | **Res:** Event detail + occurrences |
| POST | `/events` | Bearer | `events:manage` | **Res:** `201 Created` |
| PUT | `/events/{id}` | Bearer | `events:manage` | **Res:** `204 No Content` |
| DELETE| `/events/{id}` | Bearer | `events:manage` | **Res:** `204 No Content` |
| GET | `/events/{id}/occurrences` | Bearer | `events:view`\|`manage` | **Res:** List of occurrences |
| POST | `/events/{id}/occurrences` | Bearer | `events:manage` | Create single occurrence.<br>**Res:** `201 Created` |
| POST | `/events/occurrences/{occId}/headcount`| Bearer | `attendance:record`\|`events:manage` | **Req:** `{ count, unitId? }`<br>**Res:** `204 No Content` |
| GET | `/events/occurrences/{occId}/registrations`| Bearer| `events:view`\|`manage`| **Res:** PagedResult |
| POST | `/events/occurrences/{occId}/check-in`| Bearer | `attendance:record`\|`events:manage` | **Req:** `{ personId }`<br>**Res:** `204 No Content` |
| GET | `/events/attendance-report` | Bearer | `attendance:view`\|`events:view`| **Query:** `?from&to`<br>**Res:** Report data |

### Giving

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| GET | `/giving/funds` | Bearer | `giving:view`\|`manage` | **Res:** List of funds |
| POST | `/giving/funds` | Bearer | `giving:manage` | **Res:** `201 Created` |
| PUT | `/giving/funds/{id}` | Bearer | `giving:manage` | **Res:** `204 No Content` |
| GET | `/giving/batches` | Bearer | `giving:view`\|`manage` | **Query:** `?page&pageSize&status&from&to`<br>**Res:** PagedResult |
| POST | `/giving/batches` | Bearer | `giving:manage` | **Res:** `201 Created` |
| POST | `/giving/batches/{id}/close`| Bearer | `giving:manage` | **Res:** `204 No Content` |
| GET | `/giving/batches/{id}/donations`| Bearer | `giving:view`\|`manage` | **Res:** List of donations |
| POST | `/giving/batches/{id}/donations`| Bearer | `giving:manage` | **Res:** `201 Created` |
| DELETE| `/giving/batches/{id}/donations/{did}`| Bearer| `giving:manage` | **Res:** `204 No Content` |
| GET | `/giving/campaigns` | Bearer | `giving:view`\|`manage` | **Query:** `?page&pageSize`<br>**Res:** PagedResult |
| POST | `/giving/campaigns` | Bearer | `giving:manage` | **Res:** `201 Created` |
| PUT | `/giving/campaigns/{id}`| Bearer | `giving:manage` | **Res:** `204 No Content` |
| GET | `/giving/campaigns/{id}/pledges`| Bearer | `giving:view`\|`manage` | **Res:** PagedResult |
| POST | `/giving/campaigns/{id}/pledges`| Bearer | `giving:manage` | **Res:** `201 Created` |
| PUT | `/giving/pledges/{id}` | Bearer | `giving:manage` | **Res:** `204 No Content` |
| GET | `/giving/summary` | Bearer | `giving:view`\|`manage` | **Query:** `?from&to&fundId`<br>**Res:** `GivingSummaryResponse` |
| GET | `/giving/statements/{personId}`| Bearer | `giving:view`\|`manage` | **Query:** `?year`<br>**Res:** `StatementResponse` |

### Content
Headless CMS capabilities. `POST /content/pages`, `/content/posts`, and `/content/sermons` share exact same verb patterns.

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| GET | `/content/pages` | Bearer | `content:view`\|`manage` | **Query:** `?page&pageSize&status&search`<br>**Res:** PagedResult |
| GET | `/content/pages/{id}`| Bearer | `content:view`\|`manage` | **Res:** `PageResponse` |
| POST | `/content/pages` | Bearer | `content:manage` | **Res:** `201 Created` |
| PUT | `/content/pages/{id}`| Bearer | `content:manage` | **Res:** `204 No Content` |
| POST | `/content/pages/{id}/publish`| Bearer | `content:manage` | **Req:** `{ scheduledFor? }`<br>**Res:** `204 No Content` |
| POST | `/content/pages/{id}/unpublish`| Bearer| `content:manage`| **Res:** `204 No Content` |
| POST | `/content/pages/{id}/archive`| Bearer| `content:manage`| **Res:** `204 No Content` |
| DELETE| `/content/pages/{id}`| Bearer | `content:manage` | **Res:** `204 No Content` |
| GET | `/content/series` | Bearer | `content:view`\|`manage` | **Res:** List of series |
| POST | `/content/series` | Bearer | `content:manage` | **Res:** `201 Created` |
| PUT | `/content/series/{id}`| Bearer | `content:manage` | **Res:** `204 No Content` |
| GET | `/content/media` | Bearer | `content:view`\|`manage` | **Query:** `?page&pageSize&folder&type`<br>**Res:** PagedResult |
| POST | `/content/media` | Bearer | `content:manage` | **Req:** `multipart` (field: `file`)<br>**Res:** `MediaResponse` |
| PUT | `/content/media/{id}`| Bearer | `content:manage` | **Req:** `{ altText?, folder? }`<br>**Res:** `204 No Content` |
| DELETE| `/content/media/{id}`| Bearer | `content:manage` | **Res:** `204 No Content` |
| GET | `/content/menus` | Bearer | `content:view`\|`manage` | **Res:** List of menus |
| PUT | `/content/menus/{key}`| Bearer | `content:manage` | **Req:** `{ name, items: JSON }`<br>**Res:** `204 No Content` |

### Communications
Announcements, Prayer Wall, Push/Email Broadcasts.

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| GET | `/comms/announcements`| Bearer | `comms:view`\|`manage` | **Query:** `?page&pageSize&status&audience`<br>**Res:** PagedResult |
| POST | `/comms/announcements`| Bearer | `comms:manage` | **Res:** `201 Created` |
| PUT | `/comms/announcements/{id}`| Bearer| `comms:manage` | **Res:** `204 No Content` |
| DELETE| `/comms/announcements/{id}`| Bearer| `comms:manage` | **Res:** `204 No Content` |
| GET | `/comms/prayer-requests`| Bearer | `comms:view`\|`manage` | **Query:** `?page&pageSize&status&search`<br>**Res:** PagedResult |
| POST | `/comms/prayer-requests`| None/Bearer| None | Anonymous submission allowed.<br>**Res:** `201 Created` |
| PUT | `/comms/prayer-requests/{id}`| Bearer | `comms:manage` | **Req:** `{ status, assignedToUserId?, approvedForWall, answerNote? }`<br>**Res:** `204 No Content` |
| GET | `/comms/broadcasts` | Bearer | `comms:view`\|`manage` | **Query:** `?page&pageSize&channel&status`<br>**Res:** PagedResult |
| POST | `/comms/broadcasts` | Bearer | `comms:manage` | **Res:** `201 Created` |
| PUT | `/comms/broadcasts/{id}`| Bearer| `comms:manage` | **Res:** `204 No Content` |
| POST | `/comms/broadcasts/{id}/send`| Bearer| `comms:manage` | **Req:** `{ scheduledFor? }`<br>**Res:** `204 No Content` |
| DELETE| `/comms/broadcasts/{id}`| Bearer| `comms:manage` | **Res:** `204 No Content` |
| GET | `/comms/templates` | Bearer | `comms:view`\|`manage` | **Res:** List of templates |
| POST | `/comms/templates` | Bearer | `comms:manage` | **Res:** `201 Created` |
| PUT | `/comms/templates/{id}`| Bearer | `comms:manage` | **Res:** `204 No Content` |
| DELETE| `/comms/templates/{id}`| Bearer | `comms:manage` | **Res:** `204 No Content` |
| GET | `/notifications` | Bearer | None | **Query:** `?page&pageSize&unreadOnly`<br>**Res:** `{ data: NotificationResponse[], meta: { unread: number } }` |
| POST | `/notifications/{id}/read`| Bearer| None | **Res:** `204 No Content` |
| POST | `/notifications/read-all`| Bearer | None | **Res:** `204 No Content` |
| POST | `/devices` | Bearer | None | **Req:** `{ platform: "ios"\|"android"\|"web", token, appVersion? }`<br>**Res:** `204 No Content` |
| DELETE| `/devices/{token}` | Bearer | None | **Res:** `204 No Content` |

#### Audiences & Contacts (Email Marketing)
Permission: `audiences:view` / `audiences:manage`

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| GET | `/audiences` | Bearer | `audiences:view`\|`manage` | **Query:** `?page&pageSize`<br>**Res:** PagedResult |
| POST | `/audiences` | Bearer | `audiences:manage` | **Req:** `{ name, description?, doubleOptIn? }`<br>**Res:** `200 OK { id, name }` |
| GET | `/audiences/{id}` | Bearer | `audiences:view`\|`manage` | **Res:** Audience detail + contact counts |
| PATCH | `/audiences/{id}` | Bearer | `audiences:manage` | Update name / description |
| DELETE | `/audiences/{id}` | Bearer | `audiences:manage` | **Sudo required** |
| GET | `/audiences/{id}/contacts` | Bearer | `audiences:view`\|`manage` | **Query:** `?page&pageSize&q`<br>**Res:** PagedResult |
| POST | `/audiences/{id}/contacts` | Bearer | `audiences:manage` | Add single subscriber |
| POST | `/audiences/{id}/contacts/remove`| Bearer | `audiences:manage` | Remove contacts by email |
| POST | `/audiences/{id}/imports` | Bearer | `audiences:manage` | CSV contact import |
| POST | `/audiences/{id}/sync-members` | Bearer | `audiences:manage` | Sync parishioners to list |
| POST | `/audiences/estimate` | Bearer | `audiences:view`\|`manage` | **Req:** `{ listIds: [] }`<br>**Res:** `{ count: number }` |

#### Email Templates
Permission: `templates:view` / `templates:manage`

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| GET | `/templates` | Bearer | `templates:view`\|`manage` | **Query:** `?page&pageSize&q`<br>**Res:** PagedResult |
| POST | `/templates` | Bearer | `templates:manage` | **Req:** `{ name, description?, content: JSON }`<br>**Res:** `200 OK { id, name }` |
| GET | `/templates/{id}` | Bearer | `templates:view`\|`manage` | **Res:** Template detail |
| PATCH | `/templates/{id}` | Bearer | `templates:manage` | Update template |
| POST | `/templates/{id}/duplicate` | Bearer | `templates:manage` | Clones template as "(copy)" |
| DELETE | `/templates/{id}` | Bearer | `templates:manage` | Delete template |

#### Email Campaigns
Permission: `campaigns:view` / `campaigns:manage`

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| GET | `/campaigns` | Bearer | `campaigns:view`\|`manage` | **Query:** `?page&pageSize&status`<br>**Res:** PagedResult |
| POST | `/campaigns` | Bearer | `campaigns:manage` | Create draft campaign |
| GET | `/campaigns/{id}` | Bearer | `campaigns:view`\|`manage` | Campaign detail |
| PATCH | `/campaigns/{id}` | Bearer | `campaigns:manage` | Update draft setup / content / audience |
| GET | `/campaigns/{id}/report` | Bearer | `campaigns:view`\|`manage` | Delivery, opens, clicks metrics |
| POST | `/campaigns/{id}/test` | Bearer | `campaigns:manage` | Send preview test email |
| POST | `/campaigns/{id}/schedule` | Bearer | `campaigns:manage` | Schedule send for future date |
| POST | `/campaigns/{id}/send` | Bearer | `campaigns:manage` | **Sudo required** Trigger immediate blast |
| POST | `/campaigns/{id}/unschedule` | Bearer | `campaigns:manage` | Revert to draft |
| POST | `/campaigns/{id}/duplicate` | Bearer | `campaigns:manage` | Clones campaign |
| DELETE | `/campaigns/{id}` | Bearer | `campaigns:manage` | Delete campaign |

#### Forms (Admin Management)
Permission: `forms:view` / `forms:manage`

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| GET | `/forms` | Bearer | `forms:view`\|`manage` | **Query:** `?page&pageSize&q&status`<br>**Res:** PagedResult |
| POST | `/forms` | Bearer | `forms:manage` | Create new form definition |
| GET | `/forms/{id}` | Bearer | `forms:view`\|`manage` | Form detail with fields & settings |
| PATCH | `/forms/{id}` | Bearer | `forms:manage` | Update title, description, fields |
| PUT | `/forms/{id}/settings` | Bearer | `forms:manage` | Update notifications & access settings |
| PUT | `/forms/{id}/slug` | Bearer | `forms:manage` | Update custom public slug |
| POST | `/forms/{id}/publish` | Bearer | `forms:manage` | Publish form live |
| POST | `/forms/{id}/close` | Bearer | `forms:manage` | Close submissions |
| POST | `/forms/{id}/duplicate` | Bearer | `forms:manage` | Clone form definition |
| GET | `/forms/{id}/responses` | Bearer | `forms:view`\|`manage` | **Query:** `?page&pageSize`<br>**Res:** PagedResult |
| GET | `/forms/{id}/responses/export` | Bearer | `forms:view` | Download responses as CSV |
| POST | `/forms/{id}/responses/delete` | Bearer | `forms:manage` | **Sudo required** Delete responses |
| DELETE | `/forms/{id}` | Bearer | `forms:manage` | Delete form |

#### Sending Settings & Domains
Permission: `settings:manage`

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| GET | `/email/sender-profile` | Bearer | `settings:manage` | From addresses, default address, reply-to |
| PUT | `/email/sender-profile` | Bearer | `settings:manage` | Save default sender profile |
| GET | `/email/sending` | Bearer | `settings:manage` | Provider mode, rate limits, unsubscribe info |
| PUT | `/email/sending` | Bearer | `settings:manage` | Update sending options |
| GET | `/email/domains` | Bearer | `settings:manage` | List verified custom sending domains |
| POST | `/email/domains` | Bearer | `settings:manage` | Register sending domain & get DNS records |
| POST | `/email/domains/{id}/verify`| Bearer | `settings:manage` | Validate DNS SPF/DKIM records |
| DELETE | `/email/domains/{id}` | Bearer | `settings:manage` | Remove custom domain |

### Dashboard
Home overview stats for administrators.

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| GET | `/dashboard/summary` | Bearer | `dashboard:view` | **Res:** `{ members, audience, campaigns, forms, pending: { accessRequests, memberApprovals, scheduledCampaigns }, setup: { domainVerified, hasAudience, hasForm, postalAddressSet } }` |

### Public Endpoints
CDN-cacheable endpoints for the public-facing website and mobile app. Must include `X-Tenant` header (or resolved by domain).

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| GET | `/public/forms/{slug}` | Anonymous | None | Get live published form schema & styling |
| POST | `/public/forms/{slug}/responses` | Anonymous | None | Submit response to public form |
| GET | `/public/content/pages` | X-Tenant | None | **Query:** `?path=/about`<br>**Res:** `PageResponse` |
| GET | `/public/content/navigation`| X-Tenant | None | **Res:** Nav items |
| GET | `/public/content/menus/{key}`| X-Tenant | None | **Res:** `MenuResponse` |
| GET | `/public/content/posts` | X-Tenant | None | **Query:** `?page&pageSize&tag&category&featured`<br>**Res:** PagedResult |
| GET | `/public/content/posts/{slug}`| X-Tenant | None | **Res:** `PostResponse` |
| GET | `/public/content/sermons` | X-Tenant | None | **Query:** `?page&pageSize&series&preacher&search`<br>**Res:** PagedResult |
| GET | `/public/content/sermons/{slug}`| X-Tenant| None | **Res:** `SermonResponse` |
| GET | `/public/content/series` | X-Tenant | None | **Res:** List of series |
| GET | `/public/content/series/{slug}`| X-Tenant| None | **Res:** `SeriesResponse` |
| GET | `/public/comms/prayer-wall`| X-Tenant | None | **Res:** `[PrayerWallItem]` |
| POST | `/public/comms/prayer-requests`| X-Tenant| None | Anonymous prayer submission |
| GET | `/public/events` | X-Tenant | None | **Query:** `?page&pageSize&from&to`<br>**Res:** Upcoming public events |
| GET | `/public/events/{slug}`| X-Tenant | None | **Res:** Event Detail |
| GET | `/public/giving/funds`| X-Tenant | None | **Res:** Public fund list |
| POST | `/public/giving/initiate`| X-Tenant | None | **Req:** `{ fundId, amount, currency, personId?, email? }`<br>**Res:** `{ paymentIntentId, clientSecret?, redirectUrl? }` |
| POST | `/public/giving/webhook`| None | None | Payment processor webhook listener.<br>**Res:** `200 OK` |

### Platform Admin
Master system administration. Requires user to have the `IsPlatformAdmin` claim.

| Method | Path | Auth | Permission | Summary / Payload |
|---|---|---|---|---|
| GET | `/platform/tenants` | Bearer | `IsPlatformAdmin` | **Query:** `?page&pageSize&search`<br>**Res:** PagedResult |
| POST | `/platform/tenants` | Bearer | `IsPlatformAdmin` | **Req:** `{ slug, name, ownerEmail, ownerFirstName, ownerLastName, kind?, timeZone?, currency?, locale? }`<br>**Res:** `TenantResponse` |
| POST | `/platform/tenants/{id}/status/{status}`| Bearer| `IsPlatformAdmin`| Status: `Active` \| `Suspended` \| `Cancelled`<br>**Res:** `204 No Content` |

---

## 5. Key Response Shapes

### PagedResult
```typescript
interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
}
```

### Problem Details Error (RFC 9457)
```typescript
interface ErrorResponse {
  type: string;        // URI reference to error type
  title: string;       // Short, human-readable summary
  status: number;      // HTTP status code
  detail: string;      // Human-readable explanation specific to occurrence
  code: string;        // Stable internal error code (e.g., 'auth.sudo_required')
  errors?: Record<string, string[]>; // Field-specific validation errors
}
```

---

## 6. Permissions Catalogue
When creating or editing Roles, use these exact string identifiers:

- **Dashboard:** `dashboard:view`
- **Members/People:** `members:view`, `members:manage`, `members:export`, `households:manage`, `custom_fields:manage`
- **Pastoral:** `pastoral:view`, `pastoral:manage`, `stages:manage`
- **Groups:** `groups:view`, `groups:manage`
- **Events & Attendance:** `events:view`, `events:manage`, `attendance:record`, `attendance:view`
- **Giving:** `giving:view`, `giving:manage`
- **Content:** `content:view`, `content:manage`
- **Communications:** `comms:view`, `comms:manage`
- **Administration:** `settings:manage`, `users:manage`, `roles:manage`, `audit:view`, `api_keys:manage`

---

## 7. Error Code Reference

| Code | HTTP Status | Description |
|---|---|---|
| `auth.unauthorized` | 401 | Missing or invalid Bearer token. |
| `auth.forbidden` | 403 | Authenticated, but lacking required role/permissions. |
| `auth.sudo_required` | 403 | Endpoint requires a valid Sudo token in the `X-Sudo-Token` header. |
| `auth.mfa_required` | 403 | Account requires MFA code to complete login. |
| `validation.failed` | 400 | Request body failed validation checks (see `errors` object). |
| `resource.not_found` | 404 | The requested resource (ID, slug, tenant) does not exist. |
| `resource.conflict` | 409 | State conflict (e.g., email already registered). |
| `tenant.suspended` | 403 | The tenant account has been suspended by the platform administrator. |
| `rate_limit.exceeded` | 429 | Too many requests. Retry after the window resets. |
| `server.internal` | 500 | An unexpected internal platform exception occurred. |
