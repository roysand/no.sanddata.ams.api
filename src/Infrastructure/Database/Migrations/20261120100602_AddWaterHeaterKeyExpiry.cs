using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddWaterHeaterKeyExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "SubscriptionKeyExpiresOn",
                schema: "public",
                table: "WaterHeater",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            // Heaters connected before spec 015: one year after the Oslo day they were first connected. CreatedAt is
            // kept when a heater is replaced. Hjemme, connected on 2026-11-19, gets 2027-11-19 (research R2).
            migrationBuilder.Sql(
                "UPDATE public.\"WaterHeater\" " +
                "SET \"SubscriptionKeyExpiresOn\" = " +
                "((\"CreatedAt\" AT TIME ZONE 'Europe/Oslo')::date + interval '1 year')::date;");

            // The default only served the rows above; new rows always get the date from the code.
            migrationBuilder.Sql(
                "ALTER TABLE public.\"WaterHeater\" ALTER COLUMN \"SubscriptionKeyExpiresOn\" DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubscriptionKeyExpiresOn",
                schema: "public",
                table: "WaterHeater");
        }
    }
}
