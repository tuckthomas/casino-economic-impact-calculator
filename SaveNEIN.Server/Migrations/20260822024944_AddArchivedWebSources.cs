using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SaveNEIN.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddArchivedWebSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "archived_web_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    original_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    observed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    observation_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    archivebox_snapshot_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    captured_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    public_archived_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    capture_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    normalized_text = table.Column<string>(type: "text", nullable: false),
                    normalized_text_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    artifact_manifest_json = table.Column<string>(type: "jsonb", nullable: false),
                    archive_relative_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    verified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    verification_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    verification_note = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_archived_web_sources", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_archived_web_sources_archivebox_snapshot_id",
                table: "archived_web_sources",
                column: "archivebox_snapshot_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_archived_web_sources_source_key_captured_at_utc",
                table: "archived_web_sources",
                columns: new[] { "source_key", "captured_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "archived_web_sources");
        }
    }
}
