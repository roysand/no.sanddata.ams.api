using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Database.Migrations;

/// <inheritdoc />
public partial class AddElectricityCostTracking : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Activate TimescaleDB (available in the timescale/timescaledb Docker image already
        // in use, but never enabled until now - see research.md §1).
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS timescaledb;");

        // TimescaleDB requires the partitioning column to be part of any primary key, so the
        // existing single-column PK on Id is replaced with a composite (Id, Timestamp) key
        // before converting the table. migrate_data preserves the rows already ingested.
        migrationBuilder.Sql(
            """
            ALTER TABLE "public"."Measurement" DROP CONSTRAINT "PK_Measurement";
            ALTER TABLE "public"."Measurement" ADD CONSTRAINT "PK_Measurement" PRIMARY KEY ("Id", "Timestamp");
            SELECT create_hypertable('"public"."Measurement"', 'Timestamp', if_not_exists => TRUE, migrate_data => TRUE);
            """);

        // Minute/hour consumption continuous aggregates. Real-time aggregation is the default
        // (materialized_only = false), so querying either view already includes not-yet-
        // materialized raw data for the current, still-open bucket - no extra application
        // logic needed for FR-002's live current-hour requirement (research.md §2).
        // suppressTransaction: true - CREATE MATERIALIZED VIEW ... WITH (timescaledb.continuous)
        // cannot run inside a transaction block (it performs an initial refresh as part of
        // creation), and Npgsql also can't pipeline it together with other statements in one
        // batch - each one needs its own standalone Sql() call. EF Core closes the ambient
        // migration transaction around these commands and reopens one afterward for the
        // remaining operations.
        migrationBuilder.Sql(
            """
            CREATE MATERIALIZED VIEW IF NOT EXISTS "public".measurement_minute WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
            SELECT "LocationId", "MeterId", time_bucket('1 minute', "Timestamp") AS "BucketStart",
                   avg("PowerWatts") AS "AvgPowerWatts"
            FROM "public"."Measurement"
            GROUP BY "LocationId", "MeterId", time_bucket('1 minute', "Timestamp");
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            CREATE MATERIALIZED VIEW IF NOT EXISTS "public".measurement_hour WITH (timescaledb.continuous, timescaledb.materialized_only = false) AS
            SELECT "LocationId", "MeterId", time_bucket('1 hour', "Timestamp") AS "BucketStart",
                   avg("PowerWatts") AS "AvgPowerWatts"
            FROM "public"."Measurement"
            GROUP BY "LocationId", "MeterId", time_bucket('1 hour', "Timestamp");
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            SELECT add_continuous_aggregate_policy('public.measurement_minute',
                start_offset => INTERVAL '1 hour', end_offset => INTERVAL '1 minute', schedule_interval => INTERVAL '1 minute');
            """,
            suppressTransaction: true);

        migrationBuilder.Sql(
            """
            SELECT add_continuous_aggregate_policy('public.measurement_hour',
                start_offset => INTERVAL '3 hours', end_offset => INTERVAL '1 minute', schedule_interval => INTERVAL '5 minutes');
            """,
            suppressTransaction: true);

        migrationBuilder.CreateTable(
            name: "ElectricityPrice",
            schema: "public",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PriceRegion = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                HourStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PriceEurPerMwh = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ElectricityPrice", x => x.Id));

        migrationBuilder.CreateTable(
            name: "ExchangeRate",
            schema: "public",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CurrencyPair = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                RateDate = table.Column<DateOnly>(type: "date", nullable: false),
                Rate = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ExchangeRate", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_ElectricityPrice_PriceRegion_HourStartUtc",
            schema: "public",
            table: "ElectricityPrice",
            columns: new[] { "PriceRegion", "HourStartUtc" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ExchangeRate_CurrencyPair_RateDate",
            schema: "public",
            table: "ExchangeRate",
            columns: new[] { "CurrencyPair", "RateDate" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ElectricityPrice",
            schema: "public");

        migrationBuilder.DropTable(
            name: "ExchangeRate",
            schema: "public");

        // Dropping a continuous aggregate also drops its refresh policy.
        migrationBuilder.Sql(
            """
            DROP MATERIALIZED VIEW IF EXISTS "public".measurement_minute;
            DROP MATERIALIZED VIEW IF EXISTS "public".measurement_hour;
            """);

        // Converting Measurement back out of a hypertable (and restoring the single-column
        // PK) is intentionally not attempted here - TimescaleDB has no built-in "undo" for
        // create_hypertable, and reverting while preserving data would require manually
        // rebuilding the table. Treat this part of the migration as one-way in practice.
    }
}
