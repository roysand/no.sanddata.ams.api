using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddWaterHeater : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WaterHeater",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProtectedSubscriptionKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaterHeater", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaterHeater_Location_LocationId",
                        column: x => x.LocationId,
                        principalSchema: "public",
                        principalTable: "Location",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WaterHeaterPowerSample",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SampledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PowerKw = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaterHeaterPowerSample", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaterHeaterPowerSample_Location_LocationId",
                        column: x => x.LocationId,
                        principalSchema: "public",
                        principalTable: "Location",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WaterHeater_DeviceId",
                schema: "public",
                table: "WaterHeater",
                column: "DeviceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WaterHeater_LocationId",
                schema: "public",
                table: "WaterHeater",
                column: "LocationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WaterHeaterPowerSample_LocationId_SampledAtUtc",
                schema: "public",
                table: "WaterHeaterPowerSample",
                columns: new[] { "LocationId", "SampledAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WaterHeater",
                schema: "public");

            migrationBuilder.DropTable(
                name: "WaterHeaterPowerSample",
                schema: "public");
        }
    }
}
