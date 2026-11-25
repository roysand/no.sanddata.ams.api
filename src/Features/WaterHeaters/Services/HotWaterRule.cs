using Application.WaterHeaters;

namespace Features.WaterHeaters.Services;

/// <summary>
/// The hot water counting rule's numbers (spec 020, research R5). They are starting values:
/// <list type="bullet">
/// <item><c>KwhPerLitre</c> 0.035: heating 1 L from 10 °C to 40 °C is 4.186 kJ/(kg·K) × 30 K ≈ 0.0349 kWh.</item>
/// <item><c>MinFallLitres</c> 2: a 1 L fall is what standby cooling and OSO's whole-litre rounding give, while a
/// running tap draws at least 2–3 L a minute.</item>
/// </list>
/// They are checked against real data and the OSO app, and changed here if needed, in research R6. The outcome is
/// written under "Calibration results" in specs/020-water-heater-consumption/research.md.
/// </summary>
public static class HotWaterRule
{
    public static readonly HotWaterRuleValues Current = new(0.035m, 2);
}
