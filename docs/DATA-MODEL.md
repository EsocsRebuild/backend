# Data model

The platform has 43 business tables across 9 PostgreSQL schemas: one schema per module, plus the shared `audit` schema. Each module schema also contains `outbox_messages`, `number_sequences` and its own `__ef_migrations_history`.

**Columns shared by most tables**

| Column | Meaning |
|---|---|
| `id` | UUIDv7 primary key |
| `tenant_id` | Owning organisation. It is on every table marked 🏷, and queries filter on it automatically. |
| `created_at`, `created_by`, `updated_at`, `updated_by` | Stamped automatically |
| `is_deleted`, `deleted_at`, `deleted_by` | Soft delete on aggregates. Unique indexes ignore deleted rows. |
| `xmin` | PostgreSQL row version used for optimistic concurrency. It is not a real column. |

Arrows (→) are references by id. Across modules they are **not** foreign keys, by design.

## tenancy

| Table | Purpose / key columns |
|---|---|
| `tenants` | An organisation. `slug` (unique, citext), `name`, `status` (Trial/Active/Suspended/Cancelled), `kind` ("church"), `plan_code`, `time_zone`, `default_currency`, `default_locale`, contact details, `address_*` |
| `tenant_domains` | Custom website domains. `host` (unique), `is_primary`, `verification_token`, `verified_at` → tenants (FK, cascade) |
| `branches` 🏷 | Campuses and locations. `name`, `code` (unique per tenant), `is_headquarters`, `status`, contact, `address_*`, `leader_person_id` → people |
| `settings` 🏷 | Key/value JSON configuration. `key` (unique per tenant), `value` jsonb, `is_public` (exposed to website/app) |

## identity (and audit)

| Table | Purpose / key columns |
|---|---|
| `users` | Global login identity. `email` (unique, citext), `password_hash`, `security_stamp`, `status`, lockout fields, `two_factor_enabled`, `two_factor_secret` (encrypted), `recovery_code_hashes` text[], `is_platform_admin` |
| `memberships` 🏷 | A user's access to a tenant. `user_id` → users (FK), `status` (Invited/Active/Suspended), `person_id` → people, `default_branch_id`. Unique (tenant, user). |
| `roles` 🏷 | `name` (unique per tenant), `description`, `is_system`, `permissions` text[] |
| `membership_roles` | (membership_id, role_id) join table, FK to both |
| `sessions` | Signed-in devices (refresh tokens). `user_id`, `tenant_id`, `membership_id`, `client_type` (Admin/Web/Mobile), `token_hash`, `previous_token_hash`, `expires_at`, `revoked_at`, `revoked_reason`, device info |
| `api_keys` 🏷 | `name`, `prefix` (unique, public), `key_hash`, `scopes` text[], `expires_at`, `last_used_at`, `revoked_at` |
| `audit.audit_entries` | Append-only change log. `tenant_id`, `user_id`, `module`, `entity_type`, `entity_id`, `action` (Created/Updated/Deleted/SoftDeleted), `changes` jsonb (old→new, secrets redacted), `ip_address`, `user_agent`, `correlation_id`, `occurred_at` |

## people

| Table | Purpose / key columns |
|---|---|
| `people` 🏷 | Everyone known to the church. `member_number` (unique per tenant), names, `gender`, `date_of_birth`, `marital_status`, `wedding_anniversary`, `email`, phones, `address_*`, occupation, `photo_url`, `membership_status` (Visitor/Regular/Member/Inactive/Transferred/Deceased), `first_visit_date`, `membership_date`, `salvation_date`, `baptism_date`, `source`, `consent_to_contact`, `tags` text[] (GIN), `custom_fields` jsonb, `branch_id`, `household_id` → households, `household_role`, `user_id` → users |
| `membership_status_changes` 🏷 | Timeline of status changes. `person_id` (FK), `from`, `to`, `effective_date`, `reason` |
| `households` 🏷 | Families. `name`, `phone_number`, `address_*`, `primary_contact_person_id` |
| `person_notes` 🏷 | Pastoral notes. `person_id` (FK), `category`, `visibility` (Staff/Private), `body` |
| `follow_ups` 🏷 | Care tasks. `person_id` (FK), `type` (FirstTimeVisitor, NewConvert, Absentee, HospitalVisit…), `status`, `priority`, `assigned_to_user_id`, `due_date`, `notes`, `outcome`, `completed_at` |
| `custom_field_definitions` 🏷 | Tenant-defined profile fields. `key`, `label`, `field_type`, `options` text[], `is_required`, `is_active`, `sort_order` |

## groups

| Table | Purpose / key columns |
|---|---|
| `groups` 🏷 | Ministries, departments, cells, choirs, classes. `name`, `slug`, `type`, `visibility` (Public/Members/Private), `parent_group_id` (self FK, nesting), `branch_id`, `description`, `meeting_schedule`, `meeting_location`, `capacity`, `accepts_join_requests`, `is_active` |
| `group_members` 🏷 | `group_id` (FK), `person_id` → people, `role` (Leader/AssistantLeader/Secretary/Member), `status`, `joined_on`. Unique (group, person). |

## events

| Table | Purpose / key columns |
|---|---|
| `events` 🏷 | Services, meetings, conferences. `title`, `slug`, `type`, `status` (Draft/Published/Cancelled), `visibility`, descriptions, `branch_id`, `group_id` → groups, location / online URL, `starts_at`, `ends_at`, `time_zone`, `recurrence_rule` (RRULE), registration settings (`capacity`, `registration_closes_at`, `max_guests_per_registration`) |
| `occurrences` 🏷 | Concrete dates of an event (materialised 120 days ahead). `event_id` (FK), `starts_at`, `ends_at`, `status`, `check_in_code` (venue QR) |
| `registrations` 🏷 | Bookings. `occurrence_id` (FK), `event_id`, `person_id`, `user_id`, guest name/email/phone, `guests`, `status` (Confirmed/Waitlisted/Cancelled/CheckedIn), `ticket_code` (unique, QR) |
| `attendance_records` 🏷 | One row per person per occurrence. `person_id`, `checked_in_at`, `checked_out_at`, `method` (Manual/QrTicket/SelfCheckIn/Kiosk), `is_first_visit`, `guardian_person_id` |
| `head_counts` 🏷 | Aggregate counts per occurrence. `men`, `women`, `children`, `first_timers`, `online`, `total` |

## giving

| Table | Purpose / key columns |
|---|---|
| `funds` 🏷 | Tithe, Offering, Building… `name`, `code` (unique per tenant), `is_active`, `is_tax_deductible`, `is_public` (offered online), `sort_order` |
| `donations` 🏷 | A gift. `receipt_number` (unique per tenant, e.g. R2026-000123), `person_id` (null = anonymous), donor name/email, `branch_id`, `batch_id` → batches, `occurrence_id` → events, `campaign_id` → campaigns, `received_on`, `method`, `channel`, `status` (Pending/Completed/Failed/Refunded/Voided), `total_amount` numeric(18,2) + `total_currency` char(3), `reference`, `provider`/`provider_reference` (unique), `is_locked` |
| `donation_allocations` 🏷 | Split of a gift across funds. `donation_id` (FK), `fund_id` (FK), `amount`. The allocations always sum to the donation total. |
| `batches` 🏷 | Offering counting sessions. `name`, `batch_date`, `currency`, `expected_total`, `status` (Open/Closed), `closed_at`/`closed_by`. Closing a batch locks its donations. |
| `campaigns` 🏷 | Fundraising drives. `name`, `fund_id` (FK), `goal_amount`/`goal_currency`, `starts_on`, `ends_on` |
| `pledges` 🏷 | Commitments. `campaign_id` (FK), `person_id`, `amount_*`, `frequency`, `status`, `pledged_on` |

## content

| Table | Purpose / key columns |
|---|---|
| `pages` 🏷 | Website pages. `title`, `slug`, `path` (unique per tenant, e.g. /about/leadership), `parent_id` (self FK), `blocks` jsonb, `template`, `show_in_navigation`, `sort_order`, `seo_*`, `status` (Draft/Scheduled/Published/Archived), `published_at`, `scheduled_for` |
| `posts` 🏷 | News and blog posts. `slug`, `excerpt`, `body` jsonb, `cover_image_url`, `author_name`, `category`, `tags` text[], `is_featured`, SEO and publishing fields |
| `sermon_series` 🏷 | `title`, `slug`, `description`, `image_url`, `starts_on`, `ends_on` |
| `sermons` 🏷 | `title`, `slug`, `series_id` (FK), `preacher`, `preacher_person_id`, `preached_on`, `scripture_references` text[], `summary`, `notes`, `video_url`, `audio_url`, `notes_document_url`, `thumbnail_url`, `duration_seconds`, `tags`, `occurrence_id`, SEO and publishing fields |
| `media_assets` 🏷 | Uploaded files. `file_name`, `storage_key` (unique), `url`, `content_type`, `size_bytes`, `alt_text`, `folder` |
| `menus` 🏷 | `key` (main/footer…), `name`, `items` jsonb tree |

## comms

| Table | Purpose / key columns |
|---|---|
| `announcements` 🏷 | `title`, `body`, `image_url`, `link_url`, `audience` (Public/Members/Group), `group_id`, `branch_id`, `publish_at`, `expires_at`, `is_pinned`, `status` |
| `prayer_requests` 🏷 | `person_id`, `user_id`, name/email/phone, `request`, `is_anonymous`, `share_on_prayer_wall`, `approved_for_wall`, `status`, `assigned_to_user_id`, `prayed_count`, `answer_note` |
| `message_templates` 🏷 | `name`, `channel`, `subject`, `body` (with {{placeholders}}) |
| `broadcasts` 🏷 | Bulk messages. `channel` (Email/Sms/Push/InApp), `subject`, `body`, `audience` jsonb (statuses, tags, branch, group, people), `status` (Draft/Scheduled/Sending/Sent/Cancelled), `scheduled_for`, counters |
| `message_deliveries` 🏷 | Per-recipient delivery. `broadcast_id` (FK), `person_id`, `user_id`, `destination`, `status` (Pending/Sent/Failed/Skipped), `attempts`, `error`, `sent_at` |
| `devices` 🏷 | Push tokens. `user_id`, `platform` (Ios/Android/Web), `token` (unique), `app_version`, `last_seen_at` |
| `notifications` 🏷 | In-app inbox. `user_id`, `category`, `title`, `body`, `link`, `read_at` |
