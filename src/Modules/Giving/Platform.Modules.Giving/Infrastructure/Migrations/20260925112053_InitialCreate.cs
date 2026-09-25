using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Modules.Giving.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "giving");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "batches",
                schema: "giving",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    batch_date = table.Column<DateOnly>(type: "date", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: true),
                    currency = table.Column<string>(type: "char(3)", nullable: false),
                    expected_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("pk_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "funds",
                schema: "giving",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    code = table.Column<string>(type: "citext", maxLength: 16, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_tax_deductible = table.Column<bool>(type: "boolean", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("pk_funds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "number_sequences",
                schema: "giving",
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
                schema: "giving",
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
                name: "campaigns",
                schema: "giving",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    fund_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    goal_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    goal_currency = table.Column<string>(type: "char(3)", nullable: false),
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
                    table.PrimaryKey("pk_campaigns", x => x.id);
                    table.ForeignKey(
                        name: "fk_campaigns_funds_fund_id",
                        column: x => x.fund_id,
                        principalSchema: "giving",
                        principalTable: "funds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "donations",
                schema: "giving",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    donor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    donor_email = table.Column<string>(type: "citext", maxLength: 256, nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: true),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    provider_reference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    refunded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_currency = table.Column<string>(type: "char(3)", nullable: false),
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
                    table.PrimaryKey("pk_donations", x => x.id);
                    table.ForeignKey(
                        name: "fk_donations_batches_batch_id",
                        column: x => x.batch_id,
                        principalSchema: "giving",
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_donations_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalSchema: "giving",
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pledges",
                schema: "giving",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    frequency = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    pledged_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    amount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    amount_currency = table.Column<string>(type: "char(3)", nullable: false),
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
                    table.PrimaryKey("pk_pledges", x => x.id);
                    table.ForeignKey(
                        name: "fk_pledges_campaigns_campaign_id",
                        column: x => x.campaign_id,
                        principalSchema: "giving",
                        principalTable: "campaigns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "donation_allocations",
                schema: "giving",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    donation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fund_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_donation_allocations", x => x.id);
                    table.ForeignKey(
                        name: "fk_donation_allocations_donations_donation_id",
                        column: x => x.donation_id,
                        principalSchema: "giving",
                        principalTable: "donations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_donation_allocations_funds_fund_id",
                        column: x => x.fund_id,
                        principalSchema: "giving",
                        principalTable: "funds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_batches_tenant_id",
                schema: "giving",
                table: "batches",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_batches_tenant_id_batch_date",
                schema: "giving",
                table: "batches",
                columns: new[] { "tenant_id", "batch_date" });

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_fund_id",
                schema: "giving",
                table: "campaigns",
                column: "fund_id");

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_tenant_id",
                schema: "giving",
                table: "campaigns",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_donation_allocations_donation_id",
                schema: "giving",
                table: "donation_allocations",
                column: "donation_id");

            migrationBuilder.CreateIndex(
                name: "ix_donation_allocations_fund_id",
                schema: "giving",
                table: "donation_allocations",
                column: "fund_id");

            migrationBuilder.CreateIndex(
                name: "ix_donation_allocations_tenant_id",
                schema: "giving",
                table: "donation_allocations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_donations_batch_id",
                schema: "giving",
                table: "donations",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_donations_campaign_id",
                schema: "giving",
                table: "donations",
                column: "campaign_id");

            migrationBuilder.CreateIndex(
                name: "ix_donations_provider_provider_reference",
                schema: "giving",
                table: "donations",
                columns: new[] { "provider", "provider_reference" },
                unique: true,
                filter: "provider_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_donations_tenant_id",
                schema: "giving",
                table: "donations",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_donations_tenant_id_person_id_received_on",
                schema: "giving",
                table: "donations",
                columns: new[] { "tenant_id", "person_id", "received_on" });

            migrationBuilder.CreateIndex(
                name: "ix_donations_tenant_id_receipt_number",
                schema: "giving",
                table: "donations",
                columns: new[] { "tenant_id", "receipt_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_donations_tenant_id_received_on",
                schema: "giving",
                table: "donations",
                columns: new[] { "tenant_id", "received_on" });

            migrationBuilder.CreateIndex(
                name: "ix_funds_tenant_id",
                schema: "giving",
                table: "funds",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_funds_tenant_id_code",
                schema: "giving",
                table: "funds",
                columns: new[] { "tenant_id", "code" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "giving",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pledges_campaign_id_person_id",
                schema: "giving",
                table: "pledges",
                columns: new[] { "campaign_id", "person_id" });

            migrationBuilder.CreateIndex(
                name: "ix_pledges_tenant_id",
                schema: "giving",
                table: "pledges",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "donation_allocations",
                schema: "giving");

            migrationBuilder.DropTable(
                name: "number_sequences",
                schema: "giving");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "giving");

            migrationBuilder.DropTable(
                name: "pledges",
                schema: "giving");

            migrationBuilder.DropTable(
                name: "donations",
                schema: "giving");

            migrationBuilder.DropTable(
                name: "batches",
                schema: "giving");

            migrationBuilder.DropTable(
                name: "campaigns",
                schema: "giving");

            migrationBuilder.DropTable(
                name: "funds",
                schema: "giving");
        }
    }
}
