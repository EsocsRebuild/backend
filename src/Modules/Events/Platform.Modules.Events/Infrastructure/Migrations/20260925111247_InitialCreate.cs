using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Modules.Events.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "events");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "events",
                schema: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    visibility = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    cover_image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_online = table.Column<bool>(type: "boolean", nullable: false),
                    online_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    all_day = table.Column<bool>(type: "boolean", nullable: false),
                    recurrence_rule = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    registration_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: true),
                    registration_closes_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    max_guests_per_registration = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "number_sequences",
                schema: "events",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    next_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_number_sequences", x => new { x.tenant_id, x.name });
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "occurrences",
                schema: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    check_in_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occurrences", x => x.id);
                    table.ForeignKey(
                        name: "fk_occurrences_events_event_id",
                        column: x => x.event_id,
                        principalSchema: "events",
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attendance_records",
                schema: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    checked_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    checked_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_first_visit = table.Column<bool>(type: "boolean", nullable: false),
                    guardian_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_attendance_records_occurrences_occurrence_id",
                        column: x => x.occurrence_id,
                        principalSchema: "events",
                        principalTable: "occurrences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "head_counts",
                schema: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    men = table.Column<int>(type: "integer", nullable: false),
                    women = table.Column<int>(type: "integer", nullable: false),
                    children = table.Column<int>(type: "integer", nullable: false),
                    first_timers = table.Column<int>(type: "integer", nullable: false),
                    online = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_head_counts", x => x.id);
                    table.ForeignKey(
                        name: "fk_head_counts_occurrences_occurrence_id",
                        column: x => x.occurrence_id,
                        principalSchema: "events",
                        principalTable: "occurrences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "registrations",
                schema: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    phone_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    guests = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ticket_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    checked_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registrations", x => x.id);
                    table.ForeignKey(
                        name: "fk_registrations_occurrences_occurrence_id",
                        column: x => x.occurrence_id,
                        principalSchema: "events",
                        principalTable: "occurrences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_occurrence_id_person_id",
                schema: "events",
                table: "attendance_records",
                columns: new[] { "occurrence_id", "person_id" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_tenant_id",
                schema: "events",
                table: "attendance_records",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_tenant_id_person_id_checked_in_at",
                schema: "events",
                table: "attendance_records",
                columns: new[] { "tenant_id", "person_id", "checked_in_at" });

            migrationBuilder.CreateIndex(
                name: "ix_events_tenant_id",
                schema: "events",
                table: "events",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_events_tenant_id_slug",
                schema: "events",
                table: "events",
                columns: new[] { "tenant_id", "slug" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_events_tenant_id_status_starts_at",
                schema: "events",
                table: "events",
                columns: new[] { "tenant_id", "status", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_head_counts_occurrence_id",
                schema: "events",
                table: "head_counts",
                column: "occurrence_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_head_counts_tenant_id",
                schema: "events",
                table: "head_counts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_occurrences_check_in_code",
                schema: "events",
                table: "occurrences",
                column: "check_in_code");

            migrationBuilder.CreateIndex(
                name: "ix_occurrences_event_id_starts_at",
                schema: "events",
                table: "occurrences",
                columns: new[] { "event_id", "starts_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_occurrences_tenant_id",
                schema: "events",
                table: "occurrences",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_occurrences_tenant_id_starts_at",
                schema: "events",
                table: "occurrences",
                columns: new[] { "tenant_id", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "events",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_registrations_occurrence_id_status",
                schema: "events",
                table: "registrations",
                columns: new[] { "occurrence_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_registrations_tenant_id",
                schema: "events",
                table: "registrations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_registrations_tenant_id_person_id",
                schema: "events",
                table: "registrations",
                columns: new[] { "tenant_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "ix_registrations_tenant_id_user_id",
                schema: "events",
                table: "registrations",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_registrations_ticket_code",
                schema: "events",
                table: "registrations",
                column: "ticket_code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_records",
                schema: "events");

            migrationBuilder.DropTable(
                name: "head_counts",
                schema: "events");

            migrationBuilder.DropTable(
                name: "number_sequences",
                schema: "events");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "events");

            migrationBuilder.DropTable(
                name: "registrations",
                schema: "events");

            migrationBuilder.DropTable(
                name: "occurrences",
                schema: "events");

            migrationBuilder.DropTable(
                name: "events",
                schema: "events");
        }
    }
}
