using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Platform.Modules.Content.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "content");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "media_assets",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    alt_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    folder = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("pk_media_assets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "menus",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "citext", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    items = table.Column<string>(type: "jsonb", nullable: false),
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
                    table.PrimaryKey("pk_menus", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "number_sequences",
                schema: "content",
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
                schema: "content",
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
                name: "pages",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    path = table.Column<string>(type: "citext", maxLength: 500, nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    blocks = table.Column<string>(type: "jsonb", nullable: false),
                    template = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    show_in_navigation = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    seo_meta_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    seo_meta_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    seo_no_index = table.Column<bool>(type: "boolean", nullable: false),
                    seo_og_image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scheduled_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pages", x => x.id);
                    table.ForeignKey(
                        name: "fk_pages_pages_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "content",
                        principalTable: "pages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "posts",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    excerpt = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    body = table.Column<string>(type: "jsonb", nullable: false),
                    cover_image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    author_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    is_featured = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    seo_meta_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    seo_meta_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    seo_no_index = table.Column<bool>(type: "boolean", nullable: false),
                    seo_og_image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scheduled_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_posts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sermon_series",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: true),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
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
                    table.PrimaryKey("pk_sermon_series", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sermons",
                schema: "content",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_id = table.Column<Guid>(type: "uuid", nullable: true),
                    preacher = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    preacher_person_id = table.Column<Guid>(type: "uuid", nullable: true),
                    preached_on = table.Column<DateOnly>(type: "date", nullable: false),
                    scripture_references = table.Column<List<string>>(type: "text[]", nullable: false),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    notes = table.Column<string>(type: "character varying(50000)", maxLength: 50000, nullable: true),
                    video_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    audio_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    notes_document_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    thumbnail_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    occurrence_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    seo_meta_description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    seo_meta_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    seo_no_index = table.Column<bool>(type: "boolean", nullable: false),
                    seo_og_image_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scheduled_for = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sermons", x => x.id);
                    table.ForeignKey(
                        name: "fk_sermons_sermon_series_series_id",
                        column: x => x.series_id,
                        principalSchema: "content",
                        principalTable: "sermon_series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_storage_key",
                schema: "content",
                table: "media_assets",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_tenant_id",
                schema: "content",
                table: "media_assets",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_assets_tenant_id_folder",
                schema: "content",
                table: "media_assets",
                columns: new[] { "tenant_id", "folder" });

            migrationBuilder.CreateIndex(
                name: "ix_menus_tenant_id",
                schema: "content",
                table: "menus",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_menus_tenant_id_key",
                schema: "content",
                table: "menus",
                columns: new[] { "tenant_id", "key" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "content",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_pages_parent_id",
                schema: "content",
                table: "pages",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_pages_status_scheduled_for",
                schema: "content",
                table: "pages",
                columns: new[] { "status", "scheduled_for" },
                filter: "status = 'Scheduled'");

            migrationBuilder.CreateIndex(
                name: "ix_pages_tenant_id",
                schema: "content",
                table: "pages",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_pages_tenant_id_path",
                schema: "content",
                table: "pages",
                columns: new[] { "tenant_id", "path" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_pages_tenant_id_status_published_at",
                schema: "content",
                table: "pages",
                columns: new[] { "tenant_id", "status", "published_at" });

            migrationBuilder.CreateIndex(
                name: "ix_posts_status_scheduled_for",
                schema: "content",
                table: "posts",
                columns: new[] { "status", "scheduled_for" },
                filter: "status = 'Scheduled'");

            migrationBuilder.CreateIndex(
                name: "ix_posts_tags",
                schema: "content",
                table: "posts",
                column: "tags")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_posts_tenant_id",
                schema: "content",
                table: "posts",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_posts_tenant_id_slug",
                schema: "content",
                table: "posts",
                columns: new[] { "tenant_id", "slug" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_posts_tenant_id_status_published_at",
                schema: "content",
                table: "posts",
                columns: new[] { "tenant_id", "status", "published_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sermon_series_tenant_id",
                schema: "content",
                table: "sermon_series",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sermon_series_tenant_id_slug",
                schema: "content",
                table: "sermon_series",
                columns: new[] { "tenant_id", "slug" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_sermons_series_id",
                schema: "content",
                table: "sermons",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "ix_sermons_status_scheduled_for",
                schema: "content",
                table: "sermons",
                columns: new[] { "status", "scheduled_for" },
                filter: "status = 'Scheduled'");

            migrationBuilder.CreateIndex(
                name: "ix_sermons_tenant_id",
                schema: "content",
                table: "sermons",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_sermons_tenant_id_preached_on",
                schema: "content",
                table: "sermons",
                columns: new[] { "tenant_id", "preached_on" });

            migrationBuilder.CreateIndex(
                name: "ix_sermons_tenant_id_slug",
                schema: "content",
                table: "sermons",
                columns: new[] { "tenant_id", "slug" },
                unique: true,
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_sermons_tenant_id_status_published_at",
                schema: "content",
                table: "sermons",
                columns: new[] { "tenant_id", "status", "published_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_assets",
                schema: "content");

            migrationBuilder.DropTable(
                name: "menus",
                schema: "content");

            migrationBuilder.DropTable(
                name: "number_sequences",
                schema: "content");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "content");

            migrationBuilder.DropTable(
                name: "pages",
                schema: "content");

            migrationBuilder.DropTable(
                name: "posts",
                schema: "content");

            migrationBuilder.DropTable(
                name: "sermons",
                schema: "content");

            migrationBuilder.DropTable(
                name: "sermon_series",
                schema: "content");
        }
    }
}
