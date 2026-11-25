using Domain.Common;

namespace Application.EvChargers;

/// <summary>A charger the server's Zaptec sign-in can see.</summary>
public sealed record ZaptecCharger(Guid Id, string Name, Guid InstallationId, string InstallationName);

/// <summary>
/// One <c>energyDetails</c> point: the kWh delivered in the period that ends at <paramref name="TimestampUtc"/>. Not
/// cumulative, whatever Zaptec's docs say (spec 016, research R5, checked against Roy's sessions on 2026-11-20).
/// </summary>
public sealed record EnergyPoint(DateTime TimestampUtc, decimal Kwh);

/// <summary>
/// One completed session from Zaptec. <paramref name="Discard"/> is true for a voided session or one Zaptec replaced
/// with another; such a session is not stored, and is deleted if it was stored earlier.
/// </summary>
public sealed record ZaptecSession(
    Guid Id,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal Kwh,
    IReadOnlyList<EnergyPoint> Details,
    bool Discard);

/// <summary>A session's kWh in one UTC clock hour; <paramref name="Estimated"/> when spread evenly.</summary>
public sealed record ChargingHourSplit(DateTime HourStartUtc, decimal Kwh, bool Estimated);

/// <summary>A session as read, with its hours, ready to be stored.</summary>
public sealed record ChargingSessionToStore(ZaptecSession Session, IReadOnlyList<ChargingHourSplit> Hours);

/// <summary>A location's charger kWh in one UTC hour, summed over its sessions.</summary>
public sealed record EvChargerHourEnergy(DateTime HourStartUtc, decimal Kwh, bool Estimated);

/// <summary>One of Zaptec's state values and when it last changed (UTC); Zaptec keeps a value until it changes.
/// </summary>
public sealed record ZaptecStateValue<T>(T Value, DateTime TimestampUtc);

/// <summary>
/// The charger's latest reported values from Zaptec's <c>GET api/chargers/{id}/state</c> (spec 021, research R1). Only
/// these six are kept; every other value (serial, SIM numbers, signed meter data, PIN, the session id) is dropped in
/// the client (FR-006). Each is null when Zaptec didn't send it or it couldn't be read.
/// </summary>
/// <param name="Online">State -2: the charger is online (1) or not.</param>
/// <param name="LastCheckInUtc">State -1: the time of the charger's last check-in (its value isn't used).</param>
/// <param name="PowerW">State 513: total charge power now, in W.</param>
/// <param name="SessionKwh">State 553: energy delivered in the current session, in kWh.</param>
/// <param name="OperationMode">State 710: 1 no car, 2 connected, 3 charging, 5 finished.</param>
/// <param name="SessionSetUtc">State 721: when the current session id was set (the car was plugged in).</param>
public sealed record ZaptecChargerState(
    ZaptecStateValue<bool>? Online,
    DateTime? LastCheckInUtc,
    ZaptecStateValue<decimal>? PowerW,
    ZaptecStateValue<decimal>? SessionKwh,
    ZaptecStateValue<int>? OperationMode,
    DateTime? SessionSetUtc);

/// <summary>Errors of the EV charger feature. Descriptions are fixed text and never include a sign-in or token.</summary>
public static class EvChargerErrors
{
    public static readonly Error NotConfigured = Error.Problem(
        "EvCharger.NotConfigured", "The Zaptec sign-in is not set up on the server");
    public static readonly Error SignInRejected = Error.Problem(
        "EvCharger.SignInRejected", "Zaptec rejected the server's sign-in; the Zaptec login in the server's settings " +
                                    "must be updated");
    public static readonly Error Unavailable = Error.Problem(
        "EvCharger.Unavailable", "The charger service (Zaptec) is unavailable; try again later");
    public static readonly Error InvalidResponse = Error.Problem(
        "EvCharger.InvalidResponse", "The charger service (Zaptec) answered in an unexpected way");
    public static readonly Error UnknownCharger = Error.Validation(
        "EvCharger.UnknownCharger", "Zaptec does not know that charger for the server's sign-in");
    public static readonly Error ChargerInUse = Error.Conflict(
        "EvCharger.ChargerInUse", "This charger is already connected to another location");
    public static readonly Error NotConnected = Error.NotFound(
        "EvCharger.NotConnected", "No EV charger is connected to this location");

    /// <summary>
    /// A viewer changing the setting. <see cref="Error"/> has no forbidden kind, so the endpoint maps this code to 403.
    /// </summary>
    public static readonly Error Forbidden = Error.Failure(
        "EvCharger.Forbidden", "Only an owner of the location or an Admin may change this");
}
