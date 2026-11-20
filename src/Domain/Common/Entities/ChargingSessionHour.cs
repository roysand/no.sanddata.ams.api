namespace Domain.Common.Entities;

/// <summary>
/// The kWh a charging session delivered in one UTC clock hour. The location id is copied from the session so the
/// figures can read a location's charger kWh per hour with one query.
/// </summary>
public class ChargingSessionHour : Entity
{
    public Guid ChargingSessionId { get; private set; }
    public Guid LocationId { get; private set; }
    public DateTime HourStartUtc { get; private set; }
    public decimal Kwh { get; private set; }

    /// <summary>True when the session had no per-hour energy from Zaptec and its kWh was spread evenly.</summary>
    public bool Estimated { get; private set; }

    public ChargingSessionHour(
        Guid id, Guid chargingSessionId, Guid locationId, DateTime hourStartUtc, decimal kwh, bool estimated)
        : base(id)
    {
        ChargingSessionId = chargingSessionId;
        LocationId = locationId;
        HourStartUtc = hourStartUtc;
        Kwh = kwh;
        Estimated = estimated;
    }

    public ChargingSessionHour() : base() { }
}
