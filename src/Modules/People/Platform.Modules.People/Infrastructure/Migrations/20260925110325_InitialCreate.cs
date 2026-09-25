using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Modules.People.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "people");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "custom_field_definitions",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    field_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    options = table.Column<List<string>>(type: "text[]", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("pk_custom_field_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "households",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    primary_contact_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    address_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    address_country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    address_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("pk_households", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "number_sequences",
                schema: "people",
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
                schema: "people",
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
                name: "people",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    household_id = table.Column<Guid>(type: "uuid", nullable: true),
                    household_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    middle_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    preferred_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    gender = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    marital_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    wedding_anniversary = table.Column<DateOnly>(type: "date", nullable: true),
                    email = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    phone_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    alternate_phone_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    occupation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    employer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    photo_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    membership_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    first_visit_date = table.Column<DateOnly>(type: "date", nullable: true),
                    membership_date = table.Column<DateOnly>(type: "date", nullable: true),
                    salvation_date = table.Column<DateOnly>(type: "date", nullable: true),
                    baptism_date = table.Column<DateOnly>(type: "date", nullable: true),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    consent_to_contact = table.Column<bool>(type: "boolean", nullable: false),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    custom_fields = table.Column<string>(type: "jsonb", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    address_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    address_country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address_postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    address_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("pk_people", x => x.id);
                    table.ForeignKey(
                        name: "fk_people_households_household_id",
                        column: x => x.household_id,
                        principalSchema: "people",
                        principalTable: "households",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "follow_ups",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    priority = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    assigned_to_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    outcome = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("pk_follow_ups", x => x.id);
                    table.ForeignKey(
                        name: "fk_follow_ups_people_person_id",
                        column: x => x.person_id,
                        principalSchema: "people",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "membership_status_changes",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    to = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_membership_status_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_membership_status_changes_people_person_id",
                        column: x => x.person_id,
                        principalSchema: "people",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "person_notes",
                schema: "people",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    visibility = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
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
                    table.PrimaryKey("pk_person_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_person_notes_people_person_id",
                        column: x => x.person_id,
                        principalSchema: "people",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_custom_field_definitions_tenant_id",
                schema: "people",
                table: "custom_field_definitions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_custom_field_definitions_tenant_id_key",
                schema: "people",
                table: "custom_field_definitions",
                columns: new[] { "tenant_id", "key" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_assigned_to_user_id_status",
                schema: "people",
                table: "follow_ups",
                columns: new[] { "assigned_to_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_person_id",
                schema: "people",
                table: "follow_ups",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_tenant_id",
                schema: "people",
                table: "follow_ups",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_ups_tenant_id_status_due_date",
                schema: "people",
                table: "follow_ups",
                columns: new[] { "tenant_id", "status", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ix_households_tenant_id",
                schema: "people",
                table: "households",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_membership_status_changes_person_id",
                schema: "people",
                table: "membership_status_changes",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_membership_status_changes_tenant_id",
                schema: "people",
                table: "membership_status_changes",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "people",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_people_household_id",
                schema: "people",
                table: "people",
                column: "household_id");

            migrationBuilder.CreateIndex(
                name: "ix_people_tags",
                schema: "people",
                table: "people",
                column: "tags")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_people_tenant_id",
                schema: "people",
                table: "people",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_people_tenant_id_email",
                schema: "people",
                table: "people",
                columns: new[] { "tenant_id", "email" });

            migrationBuilder.CreateIndex(
                name: "ix_people_tenant_id_last_name_first_name",
                schema: "people",
                table: "people",
                columns: new[] { "tenant_id", "last_name", "first_name" });

            migrationBuilder.CreateIndex(
                name: "ix_people_tenant_id_member_number",
                schema: "people",
                table: "people",
                columns: new[] { "tenant_id", "member_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_people_tenant_id_membership_status",
                schema: "people",
                table: "people",
                columns: new[] { "tenant_id", "membership_status" });

            migrationBuilder.CreateIndex(
                name: "ix_people_tenant_id_phone_number",
                schema: "people",
                table: "people",
                columns: new[] { "tenant_id", "phone_number" });

            migrationBuilder.CreateIndex(
                name: "ix_people_tenant_id_user_id",
                schema: "people",
                table: "people",
                columns: new[] { "tenant_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_person_notes_person_id",
                schema: "people",
                table: "person_notes",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_person_notes_tenant_id",
                schema: "people",
                table: "person_notes",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "custom_field_definitions",
                schema: "people");

            migrationBuilder.DropTable(
                name: "follow_ups",
                schema: "people");

            migrationBuilder.DropTable(
                name: "membership_status_changes",
                schema: "people");

            migrationBuilder.DropTable(
                name: "number_sequences",
                schema: "people");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "people");

            migrationBuilder.DropTable(
                name: "person_notes",
                schema: "people");

            migrationBuilder.DropTable(
                name: "people",
                schema: "people");

            migrationBuilder.DropTable(
                name: "households",
                schema: "people");
        }
    }
}
