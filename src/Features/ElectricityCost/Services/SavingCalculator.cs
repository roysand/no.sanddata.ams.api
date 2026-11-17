using Application.ElectricityCost;
using Features.ElectricityCost.Queries;

namespace Features.ElectricityCost.Services;

/// <summary>
/// What Norgespris saves compared with spot terms. Pure: it only adds up hours that were priced by the one
/// calculation, so the saving can never disagree with the cost figures.
/// </summary>
public static class SavingCalculator
{
    public const string Today = "Today";
    public const string Last7Days = "Last7Days";
    public const string MonthToDate = "MonthToDate";
    public const string YearToDate = "YearToDate";

    /// <summary>
    /// The four periods, as Oslo calendar days ending today. <paramref name="hours"/> must cover from the earliest
    /// period start; hours outside a period are ignored for it.
    /// </summary>
    public static NorgesPrisSavingResponse Calculate(IReadOnlyList<HourCost> hours, DateTime today)
    {
        DateTime date = today.Date;
        var periods = new (string Name, DateTime From)[]
        {
            (Today, date),
            (Last7Days, date.AddDays(-6)),
            (MonthToDate, new DateTime(date.Year, date.Month, 1)),
            (YearToDate, new DateTime(date.Year, 1, 1))
        };

        return new NorgesPrisSavingResponse(periods.Select(p => ForPeriod(hours, p.Name, p.From, date)).ToList());
    }

    /// <summary>The earliest day any of the periods needs, so one pass over the hours serves all of them.</summary>
    public static DateTime EarliestStart(DateTime today)
    {
        DateTime date = today.Date;
        DateTime yearStart = new(date.Year, 1, 1);
        DateTime weekStart = date.AddDays(-6);
        return yearStart < weekStart ? yearStart : weekStart;
    }

    private static SavingPeriodResponse ForPeriod(
        IReadOnlyList<HourCost> hours, string name, DateTime from, DateTime to)
    {
        decimal saving = 0m;
        int counted = 0;
        int withConsumption = 0;

        foreach (HourCost hour in hours)
        {
            DateTime day = CostTime.LocalDateOf(hour.HourStart);
            if (day < from || day > to)
            {
                continue;
            }

            withConsumption++;

            // On a spot location the actual is spot terms and the comparison is Norgespris terms; the other way round
            // on a Norgespris location. Either way the saving is spot minus Norgespris.
            RateCost? spot = hour.Cost.PricingModel == PricingModel.Spot ? hour.Cost.Actual : hour.Cost.Comparison;
            RateCost? norgesPris = hour.Cost.PricingModel == PricingModel.Spot ? hour.Cost.Comparison : hour.Cost.Actual;
            if (spot is null || norgesPris is null)
            {
                continue;
            }

            saving += spot.Cost - norgesPris.Cost;
            counted++;
        }

        return new SavingPeriodResponse(name, DateOnly.FromDateTime(from), DateOnly.FromDateTime(to), saving, counted, withConsumption);
    }
}
