using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SaveNEIN.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCoalitionSignups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "coalition_signups",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    state_province = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    postal_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    display_yard_sign = table.Column<bool>(type: "boolean", nullable: false),
                    work_event_booth = table.Column<bool>(type: "boolean", nullable: false),
                    go_door_to_door = table.Column<bool>(type: "boolean", nullable: false),
                    write_letter_to_editor = table.Column<bool>(type: "boolean", nullable: false),
                    share_social_media = table.Column<bool>(type: "boolean", nullable: false),
                    work_polling_site_election_day = table.Column<bool>(type: "boolean", nullable: false),
                    make_phone_calls = table.Column<bool>(type: "boolean", nullable: false),
                    be_listed_as_supporter = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coalition_signups", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_coalition_signups_normalized_email",
                table: "coalition_signups",
                column: "normalized_email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coalition_signups");
        }
    }
}
