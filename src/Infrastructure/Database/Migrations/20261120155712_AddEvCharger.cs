using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddEvCharger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IncludeEvChargerInTotals",
                schema: "public",
                table: "Location",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ChargingSession",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZaptecSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChargerId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Kwh = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    HasEnergyDetails = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChargingSession", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChargingSession_Location_LocationId",
                        column: x => x.LocationId,
                        principalSchema: "public",
                        principalTable: "Location",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EvCharger",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChargerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChargerName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    InstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstallationName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ReadUpToUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvCharger", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvCharger_Location_LocationId",
                        column: x => x.LocationId,
                        principalSchema: "public",
                        principalTable: "Location",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChargingSessionHour",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChargingSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    HourStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Kwh = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: false),
                    Estimated = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChargingSessionHour", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChargingSessionHour_ChargingSession_ChargingSessionId",
                        column: x => x.ChargingSessionId,
                        principalSchema: "public",
                        principalTable: "ChargingSession",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChargingSession_LocationId_EndUtc",
                schema: "public",
                table: "ChargingSession",
                columns: new[] { "LocationId", "EndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ChargingSession_ZaptecSessionId",
                schema: "public",
                table: "ChargingSession",
                column: "ZaptecSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChargingSessionHour_ChargingSessionId_HourStartUtc",
                schema: "public",
                table: "ChargingSessionHour",
                columns: new[] { "ChargingSessionId", "HourStartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChargingSessionHour_LocationId_HourStartUtc",
                schema: "public",
                table: "ChargingSessionHour",
                columns: new[] { "LocationId", "HourStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EvCharger_ChargerId",
                schema: "public",
                table: "EvCharger",
                column: "ChargerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvCharger_LocationId",
                schema: "public",
                table: "EvCharger",
                column: "LocationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChargingSessionHour",
                schema: "public");

            migrationBuilder.DropTable(
                name: "EvCharger",
                schema: "public");

            migrationBuilder.DropTable(
                name: "ChargingSession",
                schema: "public");

            migrationBuilder.DropColumn(
                name: "IncludeEvChargerInTotals",
                schema: "public",
                table: "Location");
        }
    }
}
