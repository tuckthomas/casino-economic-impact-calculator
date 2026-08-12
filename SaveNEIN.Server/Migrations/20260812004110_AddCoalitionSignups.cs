using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
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
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "address_points",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SourceUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IngestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    HouseNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StreetNameRaw = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StreetNameNorm = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StreetPredir = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    StreetType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    StreetPostdir = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    State = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    Zip = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Geom = table.Column<Point>(type: "geometry(Point, 4326)", nullable: false),
                    Raw = table.Column<string>(type: "jsonb", nullable: true),
                    SourceRank = table.Column<short>(type: "smallint", nullable: false),
                    UspsDpvKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_address_points", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "block_groups",
                columns: table => new
                {
                    GeoId = table.Column<string>(type: "text", nullable: false),
                    CountyFips = table.Column<string>(type: "text", nullable: false),
                    Population = table.Column<int>(type: "integer", nullable: false),
                    MedianIncome = table.Column<int>(type: "integer", nullable: true),
                    Geom = table.Column<MultiPolygon>(type: "geometry(MultiPolygon, 4326)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_block_groups", x => x.GeoId);
                });

            migrationBuilder.CreateTable(
                name: "casino_competitors",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    county = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    venue_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    operator_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    market_notes = table.Column<string>(type: "text", nullable: true),
                    source_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    has_slots = table.Column<bool>(type: "boolean", nullable: false),
                    has_table_games = table.Column<bool>(type: "boolean", nullable: false),
                    has_poker = table.Column<bool>(type: "boolean", nullable: false),
                    has_sportsbook = table.Column<bool>(type: "boolean", nullable: false),
                    has_racetrack = table.Column<bool>(type: "boolean", nullable: false),
                    has_hotel = table.Column<bool>(type: "boolean", nullable: false),
                    has_restaurants = table.Column<bool>(type: "boolean", nullable: false),
                    has_entertainment = table.Column<bool>(type: "boolean", nullable: false),
                    has_loyalty_program = table.Column<bool>(type: "boolean", nullable: false),
                    has_resort_amenities = table.Column<bool>(type: "boolean", nullable: false),
                    estimated_competition_weight = table.Column<double>(type: "double precision", nullable: true),
                    geom = table.Column<Point>(type: "geometry(Point, 4326)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_casino_competitors", x => x.id);
                });

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

            migrationBuilder.CreateTable(
                name: "counties",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    StateFips = table.Column<string>(type: "text", nullable: false),
                    CountyFips = table.Column<string>(type: "text", nullable: false),
                    Geom = table.Column<MultiPolygon>(type: "geometry(MultiPolygon, 4326)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_counties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImpactFacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    SourceUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImpactFacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "isochrone_cache",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Lat = table.Column<double>(type: "double precision", nullable: false),
                    Lon = table.Column<double>(type: "double precision", nullable: false),
                    Minutes = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceHash = table.Column<string>(type: "text", nullable: true),
                    Geom = table.Column<MultiPolygon>(type: "geometry(MultiPolygon, 4326)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_isochrone_cache", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Legislators",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    District = table.Column<string>(type: "text", nullable: true),
                    Party = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Legislators", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "site_scores",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CountyId = table.Column<int>(type: "integer", nullable: false),
                    Lat = table.Column<double>(type: "double precision", nullable: false),
                    Lon = table.Column<double>(type: "double precision", nullable: false),
                    Minutes = table.Column<int>(type: "integer", nullable: false),
                    PopEst = table.Column<double>(type: "double precision", nullable: false),
                    IncomeEst = table.Column<double>(type: "double precision", nullable: true),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceHash = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_site_scores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tiger_address_ranges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    State = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    CountyFp = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    LFromHn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    LToHn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    RFromHn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    RToHn = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    NameNorm = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Geom = table.Column<LineString>(type: "geometry(LineString, 4326)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tiger_address_ranges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_address_points_Geom",
                table: "address_points",
                column: "Geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_address_points_Source_SourceId",
                table: "address_points",
                columns: new[] { "Source", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_address_points_State_Zip_StreetNameNorm_HouseNumber",
                table: "address_points",
                columns: new[] { "State", "Zip", "StreetNameNorm", "HouseNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_block_groups_CountyFips",
                table: "block_groups",
                column: "CountyFips");

            migrationBuilder.CreateIndex(
                name: "IX_block_groups_Geom",
                table: "block_groups",
                column: "Geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_casino_competitors_geom",
                table: "casino_competitors",
                column: "geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_coalition_signups_normalized_email",
                table: "coalition_signups",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_counties_Geom",
                table: "counties",
                column: "Geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_isochrone_cache_Geom",
                table: "isochrone_cache",
                column: "Geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_isochrone_cache_Lat_Lon_Minutes_SourceHash",
                table: "isochrone_cache",
                columns: new[] { "Lat", "Lon", "Minutes", "SourceHash" });

            migrationBuilder.CreateIndex(
                name: "IX_tiger_address_ranges_Geom",
                table: "tiger_address_ranges",
                column: "Geom")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_tiger_address_ranges_State_NameNorm",
                table: "tiger_address_ranges",
                columns: new[] { "State", "NameNorm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "address_points");

            migrationBuilder.DropTable(
                name: "block_groups");

            migrationBuilder.DropTable(
                name: "casino_competitors");

            migrationBuilder.DropTable(
                name: "coalition_signups");

            migrationBuilder.DropTable(
                name: "counties");

            migrationBuilder.DropTable(
                name: "ImpactFacts");

            migrationBuilder.DropTable(
                name: "isochrone_cache");

            migrationBuilder.DropTable(
                name: "Legislators");

            migrationBuilder.DropTable(
                name: "site_scores");

            migrationBuilder.DropTable(
                name: "tiger_address_ranges");
        }
    }
}
