using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddMeasurementAndMeter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Meter",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MeterId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    MeterType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Comment = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Meter", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Meter_Location_LocationId",
                        column: x => x.LocationId,
                        principalSchema: "public",
                        principalTable: "Location",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Measurement",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    MeterId = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PowerWatts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Measurement", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Measurement_Meter_MeterId",
                        column: x => x.MeterId,
                        principalSchema: "public",
                        principalTable: "Meter",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Measurement_LocationId_Timestamp",
                schema: "public",
                table: "Measurement",
                columns: new[] { "LocationId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Measurement_MeterId_Timestamp",
                schema: "public",
                table: "Measurement",
                columns: new[] { "MeterId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Meter_LocationId_DeviceId",
                schema: "public",
                table: "Meter",
                columns: new[] { "LocationId", "DeviceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Measurement",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Meter",
                schema: "public");
        }
    }
}
