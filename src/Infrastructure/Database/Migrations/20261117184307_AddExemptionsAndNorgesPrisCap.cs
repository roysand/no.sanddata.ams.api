using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddExemptionsAndNorgesPrisCap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                schema: "public",
                table: "PriceRate",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.AddColumn<bool>(
                name: "ElectricityTaxExempt",
                schema: "public",
                table: "Location",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsHolidayHome",
                schema: "public",
                table: "Location",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "VatExempt",
                schema: "public",
                table: "Location",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.InsertData(
                schema: "public",
                table: "PriceRate",
                columns: new[] { "Id", "AddedByUserId", "CreatedAt", "Kind", "UpdatedAt", "ValidFrom", "Value" },
                values: new object[,]
                {
                    { new Guid("b1000000-0000-0000-0000-000000000004"), new Guid("00000000-0000-0000-0000-000000000000"), new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), "NorgesPrisCapHome", new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), new DateOnly(2025, 10, 1), 5000m },
                    { new Guid("b1000000-0000-0000-0000-000000000005"), new Guid("00000000-0000-0000-0000-000000000000"), new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), "NorgesPrisCapHolidayHome", new DateTime(2026, 11, 17, 0, 0, 0, 0, DateTimeKind.Utc), new DateOnly(2025, 10, 1), 1000m }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                schema: "public",
                table: "PriceRate",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000004"));

            migrationBuilder.DeleteData(
                schema: "public",
                table: "PriceRate",
                keyColumn: "Id",
                keyValue: new Guid("b1000000-0000-0000-0000-000000000005"));

            migrationBuilder.DropColumn(
                name: "ElectricityTaxExempt",
                schema: "public",
                table: "Location");

            migrationBuilder.DropColumn(
                name: "IsHolidayHome",
                schema: "public",
                table: "Location");

            migrationBuilder.DropColumn(
                name: "VatExempt",
                schema: "public",
                table: "Location");

            migrationBuilder.AlterColumn<string>(
                name: "Kind",
                schema: "public",
                table: "PriceRate",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(40)",
                oldMaxLength: 40);
        }
    }
}
