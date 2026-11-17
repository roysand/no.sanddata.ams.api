using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddGridCapacityStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GridCapacityStep",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GridTariffId = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    LowerKw = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    UpperKw = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: true),
                    MonthlyAmount = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GridCapacityStep", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GridCapacityStep_GridTariff_GridTariffId",
                        column: x => x.GridTariffId,
                        principalSchema: "public",
                        principalTable: "GridTariff",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GridCapacityStep_GridTariffId_ValidFrom_LowerKw",
                schema: "public",
                table: "GridCapacityStep",
                columns: new[] { "GridTariffId", "ValidFrom", "LowerKw" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GridCapacityStep",
                schema: "public");
        }
    }
}
