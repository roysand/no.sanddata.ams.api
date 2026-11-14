using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddNorgesPrisRate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NorgesPrisRate",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    RatePerKwh = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    TaxPerKwh = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NorgesPrisRate", x => x.Id);
                });

            migrationBuilder.InsertData(
                schema: "public",
                table: "NorgesPrisRate",
                columns: new[] { "Id", "AddedByUserId", "CreatedAt", "RatePerKwh", "TaxPerKwh", "UpdatedAt", "ValidFrom" },
                values: new object[] { new Guid("b0000000-0000-0000-0000-000000000001"), new Guid("00000000-0000-0000-0000-000000000000"), new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Utc), 0.40m, 0m, new DateTime(2026, 11, 13, 0, 0, 0, 0, DateTimeKind.Utc), new DateOnly(2020, 1, 1) });

            migrationBuilder.CreateIndex(
                name: "IX_NorgesPrisRate_ValidFrom",
                schema: "public",
                table: "NorgesPrisRate",
                column: "ValidFrom",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NorgesPrisRate",
                schema: "public");
        }
    }
}
