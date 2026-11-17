using Domain.Common.Entities;

namespace Application.ElectricityCost;

/// <summary>The highest hour of one Oslo calendar day. The kWh of a one-hour bucket is its average kW.</summary>
public record CapacityPeak(DateOnly Date, DateTime HourStartUtc, decimal Kw);

/// <summary>
/// The capacity charge of one month. When <see cref="Set"/> is false (no step table, or no consumption) the figures
/// are null: the charge is unknown, never zero.
/// </summary>
/// <param name="Peaks">The daily peaks the average is taken over, highest first (at most three).</param>
/// <param name="Step">The step whose range holds the average.</param>
/// <param name="AmountExclVat">The monthly amount of the step, NOK excluding VAT.</param>
/// <param name="KwToNextStep">How far the average is below the next step; null on the top step.</param>
public record CapacityChargeResult(
    bool Set,
    IReadOnlyList<CapacityPeak> Peaks,
    decimal? AverageKw,
    GridCapacityStep? Step,
    decimal? AmountExclVat,
    decimal? Vat,
    decimal? Amount,
    decimal? KwToNextStep);

/// <summary>
/// The grid capacity charge (kapasitetsledd): the daily peak is the hour with the highest kWh of each Oslo day; the
/// three highest daily peaks (on three different days, or as many days as there are) are averaged; the step whose range
/// holds the average, from its lower bound up to but not including its upper bound, gives the monthly amount. Pure: no
/// repositories, no rounding.
/// </summary>
public static class CapacityCharge
{
    private const int PeaksAveraged = 3;

    /// <summary>
    /// The step table of a tariff that applies to a month: the one with the latest start date on or before the first
    /// day of the month. Empty when none has started by then.
    /// </summary>
    public static IReadOnlyList<GridCapacityStep> TableFor(IEnumerable<GridCapacityStep> allSteps, DateOnly monthStart)
    {
        DateOnly? validFrom = allSteps
            .Where(s => s.ValidFrom <= monthStart)
            .Select(s => (DateOnly?)s.ValidFrom)
            .Max();

        return validFrom is null
            ? []
            : allSteps.Where(s => s.ValidFrom == validFrom).OrderBy(s => s.LowerKw).ToList();
    }

    /// <param name="hours">The kWh of each hour of the month (one entry per hour, all meters summed).</param>
    /// <param name="table">The step table of the month, see <see cref="TableFor"/>.</param>
    /// <param name="vatFraction">VAT on the first day of the month, or 0 for a VAT-exempt location.</param>
    public static CapacityChargeResult Calculate(
        IEnumerable<(DateTime HourStartUtc, decimal Kwh)> hours,
        IReadOnlyList<GridCapacityStep> table,
        decimal vatFraction)
    {
        List<CapacityPeak> peaks = hours
            .GroupBy(h => NorgesPrisSchedule.OsloDateOf(h.HourStartUtc))
            .Select(day =>
            {
                (DateTime hourStartUtc, decimal kwh) = day.OrderByDescending(h => h.Kwh).ThenBy(h => h.HourStartUtc).First();
                return new CapacityPeak(day.Key, hourStartUtc, kwh);
            })
            .OrderByDescending(p => p.Kw)
            .ThenBy(p => p.Date)
            .Take(PeaksAveraged)
            .ToList();

        if (peaks.Count == 0 || table.Count == 0)
        {
            return new CapacityChargeResult(false, peaks, null, null, null, null, null, null);
        }

        decimal average = peaks.Sum(p => p.Kw) / peaks.Count;
        GridCapacityStep? step = table.FirstOrDefault(s => average >= s.LowerKw && (s.UpperKw is null || average < s.UpperKw));
        if (step is null)
        {
            // Only when the table does not cover the average (it starts above 0); the validator does not allow that.
            return new CapacityChargeResult(false, peaks, average, null, null, null, null, null);
        }

        decimal vat = step.MonthlyAmount * vatFraction;
        return new CapacityChargeResult(
            true,
            peaks,
            average,
            step,
            step.MonthlyAmount,
            vat,
            step.MonthlyAmount + vat,
            step.UpperKw - average);
    }
}
