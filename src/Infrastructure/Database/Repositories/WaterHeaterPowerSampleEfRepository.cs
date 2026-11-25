using Application.Common.Interfaces.Repositories;
using Application.WaterHeaters;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

/// <remarks>
/// Two raw SQL queries. <see cref="QueryStepsAsync"/> serves both the hourly view (spec 012) and the 5-minute view
/// (spec 017); sharing it keeps one copy of the hold rule, so the 5-minute steps add up to the hour (spec 017,
/// research R1). <see cref="GetHourlyHotWaterAsync"/> counts the hot water used (spec 020, plan "Complexity
/// Tracking"): it asks about the change between two samples, not power over time, so it is kept apart.
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
    /// Spec 020, research R4. Pairing each sample with the one before it needs <c>LAG</c>, which LINQ to EF can't
    /// express, and a year is about 525,000 samples that must not be loaded into memory. Every value is a parameter
    /// of the interpolated query, the rule's numbers too, so calibration changes only a C# constant.
    /// <list type="bullet">
    /// <item><c>s</c>: each sample with the time, power and litres of the sample before it. Samples from 2 minutes
    /// before <c>from</c> are read so the first pair has its earlier sample; none from now or <c>to</c> on.</item>
    /// <item><c>pairs</c>: the pairs whose later sample is from <c>from</c> on, both with litres, at most 2 minutes
    /// apart (and not in the same instant). The fall counts, with the heating of the earlier sample's power over the
    /// pair turned back into litres, only when it is at least the rule's minimum; otherwise the pair is sampled time
    /// with nothing used. A pair goes to the UTC hour of its later sample.</item>
    /// </list>
    /// </remarks>
    public async Task<IReadOnlyList<WaterHeaterHotWaterHour>> GetHourlyHotWaterAsync(
        Guid locationId, DateTime fromUtc, DateTime toUtc, DateTime nowUtc, HotWaterRuleValues rule,
        CancellationToken ct)
    {
        List<WaterHeaterHotWaterHour> rows = await _context.Database
            .SqlQuery<WaterHeaterHotWaterHour>(
                $"""
                WITH s AS (
                    SELECT "SampledAtUtc" AS t,
                           "AvailableLitres40" AS l,
                           LAG("SampledAtUtc") OVER (ORDER BY "SampledAtUtc") AS pt,
                           LAG("PowerKw") OVER (ORDER BY "SampledAtUtc") AS pp,
                           LAG("AvailableLitres40") OVER (ORDER BY "SampledAtUtc") AS pl
                    FROM public."WaterHeaterPowerSample"
                    WHERE "LocationId" = {locationId}
                      AND "SampledAtUtc" >= {fromUtc} - interval '2 minutes'
                      AND "SampledAtUtc" < LEAST({toUtc}, {nowUtc})
                ),
                pairs AS (
                    SELECT date_trunc('hour', t, 'UTC') AS h,
                           EXTRACT(EPOCH FROM t - pt) AS secs,
                           CASE WHEN pl - l >= {rule.MinFallLitres}
                                THEN (pl - l) + pp * EXTRACT(EPOCH FROM t - pt) / 3600 / {rule.KwhPerLitre}
                                ELSE 0 END AS used
                    FROM s
                    WHERE t >= {fromUtc}
                      AND l IS NOT NULL
                      AND pl IS NOT NULL
                      AND t > pt
                      AND t - pt <= interval '2 minutes'
                )
                SELECT h AS "HourStartUtc",
                       SUM(used) AS "Litres",
                       SUM(secs) AS "SampledSeconds"
                FROM pairs
                GROUP BY h
                ORDER BY h
                """)
            .ToListAsync(ct);

        return rows
            .Select(r => r with { HourStartUtc = DateTime.SpecifyKind(r.HourStartUtc, DateTimeKind.Utc) })
            .ToList();
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
