namespace Application.EvChargers;

/// <summary>
/// The charger's state right now, worked out from Zaptec's latest values at the time of the request (spec 021,
/// research R2). Only for showing what is happening: it is never stored, and never added to any usage, cost, bill or
/// Norgespris figure (FR-004), since spec 016 stores the charge once it ends.
/// </summary>
/// <param name="State">One of <see cref="States"/>.</param>
/// <param name="Online">Null only with "no charger".</param>
/// <param name="PowerKw">Only while charging: Zaptec keeps the last power after charging stops.</param>
/// <param name="SessionKwh">With a car connected (or an unknown mode): the charge's kWh so far.</param>
/// <param name="PluggedInAtUtc">When the current session started (the car was plugged in).</param>
/// <param name="ChargingSinceUtc">When charging last started; after a pause, when it resumed.</param>
/// <param name="LastReportedAtUtc">The charger's last check-in.</param>
/// <param name="ReadAtUtc">When Zaptec was read; the answer is shared for up to 3 minutes.</param>
public sealed record ChargerLiveState(
    string State,
    bool? Online,
    decimal? PowerKw,
    decimal? SessionKwh,
    DateTime? PluggedInAtUtc,
    DateTime? ChargingSinceUtc,
    DateTime? LastReportedAtUtc,
    DateTime? ReadAtUtc)
{
    /// <summary>A charger with no check-in for longer than this is offline, whatever its online flag says.</summary>
    public static readonly TimeSpan OfflineAfter = TimeSpan.FromMinutes(10);

    public static class States
    {
        public const string NoCharger = "noCharger";
        public const string Offline = "offline";
        public const string NoCar = "noCar";
        public const string Connected = "connected";
        public const string Charging = "charging";
        public const string Finished = "finished";
        public const string Unknown = "unknown";
    }

    /// <summary>The answer for a location without a charger: Zaptec isn't asked.</summary>
    public static ChargerLiveState NoCharger() => new(States.NoCharger, null, null, null, null, null, null, null);

    /// <summary>Applies the rules in spec 021's data-model.md, in order.</summary>
    public static ChargerLiveState From(ZaptecChargerState values, DateTime readAtUtc, DateTime nowUtc)
    {
        DateTime? checkIn = values.LastCheckInUtc;

        // 1. Offline: the flag says so, or the charger has stopped checking in. A missing flag with a fresh check-in
        //    counts as online.
        if (values.Online is { Value: false } || checkIn is null || nowUtc - checkIn.Value > OfflineAfter)
        {
            return new ChargerLiveState(States.Offline, false, null, null, null, null, checkIn, readAtUtc);
        }

        // 2. The state from the operation mode (710).
        string state = values.OperationMode?.Value switch
        {
            1 => States.NoCar,
            2 => States.Connected,
            3 => States.Charging,
            5 => States.Finished,
            _ => States.Unknown,
        };
        bool charging = state == States.Charging;
        bool hasCar = state is States.Connected or States.Charging or States.Finished;

        // 3. Power only while charging.
        decimal? powerKw = charging && values.PowerW is { } power
            ? Math.Round(power.Value / 1000m, 2, MidpointRounding.AwayFromZero)
            : null;

        // 4. The charge's kWh; a value older than the session belongs to the previous charge.
        decimal? sessionKwh = state != States.NoCar && values.SessionKwh is { } kwh
            ? values.SessionSetUtc is { } sessionSet && kwh.TimestampUtc < sessionSet
                ? 0m
                : Math.Round(kwh.Value, 3, MidpointRounding.AwayFromZero)
            : null;

        // 5. Plugged in at, with a car; 6. charging since, while charging.
        return new ChargerLiveState(
            state,
            true,
            powerKw,
            sessionKwh,
            hasCar ? values.SessionSetUtc : null,
            charging ? values.OperationMode!.TimestampUtc : null,
            checkIn,
            readAtUtc);
    }
}
