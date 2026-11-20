namespace Application.WaterHeaters;

/// <summary>The water heater's part of one hour: kWh capped at the hour's kWh, and its share of the hour's cost.</summary>
public sealed record WaterHeaterHour(DateTime HourStartUtc, decimal Kwh, decimal? Cost, int SampledMinutes, bool Capped);

/// <summary>The sum of the heater parts of a day or a month; never projected.</summary>
public sealed record WaterHeaterTotal(
    decimal Kwh,
    decimal? Cost,
    int SampledMinutes,
    bool Capped,
    DateTime FirstHourUtc,
    DateTime LastHourUtc);

/// <summary>
/// The pure rules for the water heater's part of the cost figures (spec 012, research R3/R4). It takes plain values,
/// not the cost slice's types, so it stays in Application.
/// </summary>
public static class WaterHeaterShare
{
    private const int MinutesPerHour = 60;

    /// <summary>
    /// The heater's part of an hour. Its kWh is capped at the hour's metered kWh, so the rest is never negative, and its
    /// cost is the hour's actual cost times the heater's share of the hour's kWh, so it pays the hour's own average
    /// price, including how the Norgespris cap splits the hour. No rounding.
    /// </summary>
    public static WaterHeaterHour ForHour(WaterHeaterHourEnergy energy, decimal hourKwh, decimal? hourActualCost)
    {
        bool capped = energy.Kwh > hourKwh;
        decimal kwh = capped ? hourKwh : energy.Kwh;
        decimal? cost = hourActualCost switch
        {
            null => null,
            _ when hourKwh == 0m => 0m,
            { } total => total * kwh / hourKwh,
        };
        int minutes = (int)Math.Clamp(Math.Floor(energy.SampledSeconds / 60m), 0m, MinutesPerHour);
        return new WaterHeaterHour(energy.HourStartUtc, kwh, cost, minutes, capped);
    }

    /// <summary>
    /// The sum of the hours that have a heater part, or null when none has: no part is never shown as zeros. The cost is
    /// null when any summed hour has no cost, the same rule as a day's own cost.
    /// </summary>
    public static WaterHeaterTotal? Sum(IEnumerable<WaterHeaterHour?> hours)
    {
        var parts = hours.OfType<WaterHeaterHour>().ToList();
        if (parts.Count == 0)
        {
            return null;
        }

        decimal? cost = parts.Any(p => p.Cost is null) ? null : parts.Sum(p => p.Cost!.Value);
        return new WaterHeaterTotal(
            parts.Sum(p => p.Kwh),
            cost,
            parts.Sum(p => p.SampledMinutes),
            parts.Any(p => p.Capped),
            parts.Min(p => p.HourStartUtc),
            parts.Max(p => p.HourStartUtc));
    }
}
