namespace Application.ElectricityCost;

/// <summary>
/// The Norgespris monthly cap: the kWh of an Oslo calendar month up to the cap are priced at the Norgespris rate, the
/// rest on spot terms. Pure: no repositories, no rounding.
/// </summary>
public static class NorgesPrisCap
{
    /// <param name="monthKwhBefore">The kWh the location used in the same Oslo month before this hour.</param>
    /// <param name="hourKwh">The kWh of this hour.</param>
    /// <param name="cap">The cap in kWh for the month, or null when no cap applies.</param>
    /// <returns>The kWh of the hour within the cap and above it; they add up to <paramref name="hourKwh"/>.</returns>
    public static (decimal WithinCap, decimal AboveCap) Split(decimal monthKwhBefore, decimal hourKwh, decimal? cap)
    {
        if (cap is null)
        {
            return (hourKwh, 0m);
        }

        decimal room = Math.Max(0m, cap.Value - monthKwhBefore);
        decimal within = Math.Min(hourKwh, room);
        return (within, hourKwh - within);
    }

    /// <summary>
    /// Whether the next kWh after <paramref name="monthKwhBefore"/> is above the cap. Used for an hour with no
    /// consumption, so its rate is the rate the next kWh would get.
    /// </summary>
    public static bool IsPassed(decimal monthKwhBefore, decimal? cap) => cap is not null && monthKwhBefore >= cap.Value;
}
