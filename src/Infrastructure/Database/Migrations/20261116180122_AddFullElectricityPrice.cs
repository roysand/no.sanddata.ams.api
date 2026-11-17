using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddFullElectricityPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GridTariffId",
                schema: "public",
                table: "Location",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SupplierMarkupPerKwh",
                schema: "public",
                table: "Location",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "GridTariff",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GridTariff", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PriceRate",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceRate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GridTariffRate",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GridTariffId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    DayRatePerKwh = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    NightRatePerKwh = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GridTariffRate", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GridTariffRate_GridTariff_GridTariffId",
                        column: x => x.GridTariffId,
                        principalSchema: "public",
                        principalTable: "GridTariff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "public",
                table: "PriceRate",
                columns: new[] { "Id", "AddedByUserId", "CreatedAt", "Kind", "UpdatedAt", "ValidFrom", "Value" },
                values: new object[,]
                {
                    { new Guid("b1000000-0000-0000-0000-000000000001"), new Guid("00000000-0000-0000-0000-000000000000"), new DateTime(2026, 11, 16, 0, 0, 0, 0, DateTimeKind.Utc), "Vat", new DateTime(2026, 11, 16, 0, 0, 0, 0, DateTimeKind.Utc), new DateOnly(2020, 1, 1), 0.25m },
                    { new Guid("b1000000-0000-0000-0000-000000000002"), new Guid("00000000-0000-0000-0000-000000000000"), new DateTime(2026, 11, 16, 0, 0, 0, 0, DateTimeKind.Utc), "ElectricityTax", new DateTime(2026, 11, 16, 0, 0, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 1, 1), 0.0713m },
                    { new Guid("b1000000-0000-0000-0000-000000000003"), new Guid("00000000-0000-0000-0000-000000000000"), new DateTime(2026, 11, 16, 0, 0, 0, 0, DateTimeKind.Utc), "EnovaFee", new DateTime(2026, 11, 16, 0, 0, 0, 0, DateTimeKind.Utc), new DateOnly(2026, 1, 1), 0.01m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Location_GridTariffId",
                schema: "public",
                table: "Location",
                column: "GridTariffId");

            migrationBuilder.CreateIndex(
                name: "IX_GridTariff_Name",
                schema: "public",
                table: "GridTariff",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GridTariffRate_GridTariffId_ValidFrom",
                schema: "public",
                table: "GridTariffRate",
                columns: new[] { "GridTariffId", "ValidFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceRate_Kind_ValidFrom",
                schema: "public",
                table: "PriceRate",
                columns: new[] { "Kind", "ValidFrom" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Location_GridTariff_GridTariffId",
                schema: "public",
                table: "Location",
                column: "GridTariffId",
                principalSchema: "public",
                principalTable: "GridTariff",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Location_GridTariff_GridTariffId",
                schema: "public",
                table: "Location");

            migrationBuilder.DropTable(
                name: "GridTariffRate",
                schema: "public");

            migrationBuilder.DropTable(
                name: "PriceRate",
                schema: "public");

            migrationBuilder.DropTable(
                name: "GridTariff",
                schema: "public");

            migrationBuilder.DropIndex(
                name: "IX_Location_GridTariffId",
                schema: "public",
                table: "Location");

            migrationBuilder.DropColumn(
                name: "GridTariffId",
                schema: "public",
                table: "Location");

            migrationBuilder.DropColumn(
                name: "SupplierMarkupPerKwh",
                schema: "public",
                table: "Location");
        }
    }
}
