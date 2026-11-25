using Application.Common.Interfaces.Repositories;
using Application.WaterHeaters;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

/// <remarks>
/// Two raw SQL queries. <see cref="QueryStepsAsync"/> serves both the hourly view (spec 012) and the 5-minute view
/// (spec 017); sharing it keeps one copy of the hold rule, so the 5-minute steps add up to the hour (spec 017,
/// research R1). <see cref="GetHourlyHotWaterAsync"/> counts the hot water used (spec 020) from that hourly power and
/// a second query for the litres at each hour's start and end.
/// </remarks>
public class WaterHeaterPowerSampleEfRepository
    : GenericEfRepository<WaterHeaterPowerSample>, IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>
{
    public WaterHeaterPowerSampleEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }

    public async Task<IReadOnlyList<WaterHeaterHourEnergy>> GetHourlyEnergyAsync(
        Guid locationId, DateTime fromUtc, DateTime toUtc, DateTime nowUtc, CancellationToken ct)
    {
        List<WaterHeaterPowerStep> rows =
            await QueryStepsAsync(locationId, fromUtc, toUtc, nowUtc, TimeSpan.FromHours(1), ct);

        return rows
            .Select(r => new WaterHeaterHourEnergy(
                DateTime.SpecifyKind(r.StepStartUtc, DateTimeKind.Utc), r.Kwh, r.SampledSeconds))
            .ToList();
    }

    public async Task<IReadOnlyList<WaterHeaterPowerStep>> GetPowerStepsAsync(
        Guid locationId, DateTime fromUtc, DateTime toUtc, DateTime nowUtc, CancellationToken ct)
    {
        List<WaterHeaterPowerStep> rows =
            await QueryStepsAsync(locationId, fromUtc, toUtc, nowUtc, TimeSpan.FromMinutes(5), ct);

        return rows
            .Select(r => r with { StepStartUtc = DateTime.SpecifyKind(r.StepStartUtc, DateTimeKind.Utc) })
            .ToList();
    }

    /// <remarks>
    /// Spec 020, research R6. Two reads, then <see cref="HotWaterBalance"/> counts each hour:
    /// <list type="bullet">
    /// <item>The heated kWh and sampled time per hour come from the power query's hold rule (spec 012).</item>
    /// <item>The first and last litres per UTC hour come from one grouped query over the samples that have litres.
    /// It starts one hour before <c>from</c>, so the first hour has the litres it starts from, and reads nothing from
    /// now or <c>to</c> on. A year is about 525,000 samples, so the grouping runs in the database and returns about
    /// 8,800 rows.</item>
    /// </list>
    /// </remarks>
    public async Task<IReadOnlyList<WaterHeaterHotWaterHour>> GetHourlyHotWaterAsync(
        Guid locationId, DateTime fromUtc, DateTime toUtc, DateTime nowUtc, HotWaterRuleValues rule,
        CancellationToken ct)
    {
        IReadOnlyList<WaterHeaterHourEnergy> energy =
            await GetHourlyEnergyAsync(locationId, fromUtc, toUtc, nowUtc, ct);

        List<WaterHeaterLitresHour> rows = await _context.Database
            .SqlQuery<WaterHeaterLitresHour>(
                $"""
                SELECT date_trunc('hour', "SampledAtUtc", 'UTC') AS "HourStartUtc",
                       (array_agg("AvailableLitres40" ORDER BY "SampledAtUtc"))[1] AS "FirstLitres",
                       (array_agg("AvailableLitres40" ORDER BY "SampledAtUtc" DESC))[1] AS "LastLitres"
                FROM public."WaterHeaterPowerSample"
                WHERE "LocationId" = {locationId}
                  AND "AvailableLitres40" IS NOT NULL
                  AND "SampledAtUtc" >= {fromUtc} - interval '1 hour'
                  AND "SampledAtUtc" < LEAST({toUtc}, {nowUtc})
                GROUP BY 1
                ORDER BY 1
                """)
            .ToListAsync(ct);

        var litres = rows
            .Select(r => r with { HourStartUtc = DateTime.SpecifyKind(r.HourStartUtc, DateTimeKind.Utc) })
            .ToList();

        return HotWaterBalance.Count(litres, energy, fromUtc, rule);
    }

    /// <remarks>
    /// The power query (spec 012, plan "Complexity Tracking", research R1/R2; made step-sized in spec 017,
    /// research R1). The hold rule needs each sample's next sample (<c>LEAD</c>), which LINQ to EF can't express, and a
    /// year view must not load about a million samples into memory. Every value is a parameter of the interpolated
    /// query; Npgsql sends <paramref name="step"/> as an <c>interval</c>.
    /// <list type="bullet">
    /// <item><c>s</c>: each sample's hold ends at the next sample, 2 minutes after it, now or <c>to</c>, whichever comes
    /// first. Samples from 2 minutes before <c>from</c> are read so the first step's start is covered.</item>
    /// <item><c>held</c>: holds are clipped to start at <c>from</c>; empty holds (two samples in the same second) are
    /// dropped.</item>
    /// <item><c>parts</c>: a hold is at most 2 minutes, so with <paramref name="step"/> ≥ 2 minutes it crosses at most
    /// one step boundary; the part before the boundary goes to its own step and the rest to the next. Steps are
    /// bucketed with <c>date_bin</c> from the Unix epoch, which needs PostgreSQL 14+.</item>
    /// </list>
    /// </remarks>
    private async Task<List<WaterHeaterPowerStep>> QueryStepsAsync(
        Guid locationId, DateTime fromUtc, DateTime toUtc, DateTime nowUtc, TimeSpan step, CancellationToken ct) =>
        await _context.Database
            .SqlQuery<WaterHeaterPowerStep>(
                $"""
                WITH s AS (
                    SELECT "SampledAtUtc" AS t,
                           "PowerKw" AS p,
                           LEAST(
                               COALESCE(LEAD("SampledAtUtc") OVER (ORDER BY "SampledAtUtc"), {nowUtc}),
                               "SampledAtUtc" + interval '2 minutes',
                               {nowUtc},
                               {toUtc}) AS e
                    FROM public."WaterHeaterPowerSample"
                    WHERE "LocationId" = {locationId}
                      AND "SampledAtUtc" >= {fromUtc} - interval '2 minutes'
                      AND "SampledAtUtc" < {toUtc}
                ),
                held AS (
                    SELECT GREATEST(t, {fromUtc}) AS t, e, p
                    FROM s
                    WHERE e > GREATEST(t, {fromUtc})
                ),
                parts AS (
                    SELECT date_bin({step}, t, TIMESTAMPTZ '1970-01-01 00:00:00+00') AS h,
                           t,
                           LEAST(e, date_bin({step}, t, TIMESTAMPTZ '1970-01-01 00:00:00+00') + {step}) AS e,
                           p
                    FROM held
                    UNION ALL
                    SELECT date_bin({step}, t, TIMESTAMPTZ '1970-01-01 00:00:00+00') + {step} AS h,
                           date_bin({step}, t, TIMESTAMPTZ '1970-01-01 00:00:00+00') + {step} AS t,
                           e,
                           p
                    FROM held
                    WHERE e > date_bin({step}, t, TIMESTAMPTZ '1970-01-01 00:00:00+00') + {step}
                )
                SELECT h AS "StepStartUtc",
                       SUM(p * EXTRACT(EPOCH FROM e - t) / 3600) AS "Kwh",
                       SUM(EXTRACT(EPOCH FROM e - t)) AS "SampledSeconds"
                FROM parts
                GROUP BY h
                ORDER BY h
                """)
            .ToListAsync(ct);
}
