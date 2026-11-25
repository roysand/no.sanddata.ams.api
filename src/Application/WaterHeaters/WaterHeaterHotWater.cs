namespace Application.WaterHeaters;

/// <summary>
/// The numbers of the hot water counting rule (spec 020, research R6). An hour's water is its fall in litres plus the
/// heating done in it turned back into litres at <paramref name="KwhPerLitre"/>; an hour under
/// <paramref name="MinHourLitres"/> counts 0.
/// </summary>
/// <param name="KwhPerLitre">kWh that make one litre of 40 °C water, heat losses included.</param>
/// <param name="MinHourLitres">The fewest litres an hour must reach to count; below it, standby cooling is taken for
/// noise and the hour counts 0.</param>
public sealed record HotWaterRuleValues(decimal KwhPerLitre, int MinHourLitres);

/// <summary>
/// The first and last stored litres of one UTC hour, from its samples that have litres (spec 020, research R6). Like
/// <see cref="WaterHeaterPowerStep"/>, the property names are the column aliases of the repository's SQL query.
/// </summary>
public sealed record WaterHeaterLitresHour(DateTime HourStartUtc, int FirstLitres, int LastLitres);

/// <summary>
/// The 40 °C litres used in one UTC hour (spec 020, research R6). One row per hour that has samples with litres.
/// <see cref="Litres"/> is unrounded and ≥ 0. <see cref="SampledSeconds"/> is the hour's sampled time by the power
/// view's hold rule (spec 012), at most 3600.
/// </summary>
public sealed record WaterHeaterHotWaterHour(DateTime HourStartUtc, decimal Litres, decimal SampledSeconds);
