namespace Application.WaterHeaters;

/// <summary>
/// Counts the hot water used per UTC hour as an energy balance (spec 020, research R6):
/// <c>litres at the start − litres at the end + heated kWh ÷ KwhPerLitre</c>.
/// <list type="bullet">
/// <item>The start is the last litres of the hour before, or the hour's own first litres when the hour before has
/// none.</item>
/// <item>The heated kWh are the hour's power over time by the hold rule (spec 012), so water drawn while the heater
/// runs is counted even when the litres rise.</item>
/// <item>A result under <see cref="HotWaterRuleValues.MinHourLitres"/>, negative ones included, counts 0.</item>
/// </list>
/// OSO's litres and power change in steps every few minutes, not every minute, so a whole hour is counted at once
/// rather than pairs of samples: the steps cancel out within the hour.
/// </summary>
public static class HotWaterBalance
{
    /// <param name="litres">First and last litres per hour, ordered by hour. May start one hour before
    /// <paramref name="fromUtc"/>; that hour only gives the next one its start.</param>
    /// <param name="energy">Heated kWh and sampled seconds per hour, from the hold rule.</param>
    /// <param name="fromUtc">The first hour to count.</param>
    /// <param name="rule">The rule's numbers.</param>
    /// <returns>One row per hour from <paramref name="fromUtc"/> on that has litres, ordered by hour.</returns>
    public static IReadOnlyList<WaterHeaterHotWaterHour> Count(
        IReadOnlyList<WaterHeaterLitresHour> litres, IReadOnlyList<WaterHeaterHourEnergy> energy, DateTime fromUtc,
        HotWaterRuleValues rule)
    {
        var byHour = litres.ToDictionary(l => l.HourStartUtc);
        var energyByHour = energy.ToDictionary(e => e.HourStartUtc);

        var hours = new List<WaterHeaterHotWaterHour>();
        foreach (WaterHeaterLitresHour hour in litres.Where(l => l.HourStartUtc >= fromUtc))
        {
            int start = byHour.TryGetValue(hour.HourStartUtc.AddHours(-1), out WaterHeaterLitresHour? before)
                ? before.LastLitres
                : hour.FirstLitres;
            energyByHour.TryGetValue(hour.HourStartUtc, out WaterHeaterHourEnergy? heated);

            decimal used = start - hour.LastLitres + (heated?.Kwh ?? 0m) / rule.KwhPerLitre;
            hours.Add(new WaterHeaterHotWaterHour(
                hour.HourStartUtc, used < rule.MinHourLitres ? 0m : used, heated?.SampledSeconds ?? 0m));
        }

        return hours;
    }
}
