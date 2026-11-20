namespace Domain.Common.Entities;

/// <summary>
/// One completed charging session read from Zaptec (spec 016), with its kWh per clock hour. It belongs to the
/// location, not the connection, and is kept when the charger is removed. A session read again replaces this one.
/// </summary>
public class ChargingSession : Entity
{
    public Guid LocationId { get; private set; }
    public Guid ZaptecSessionId { get; private set; }
    public Guid ChargerId { get; private set; }
    public DateTime StartUtc { get; private set; }
    public DateTime EndUtc { get; private set; }
    public decimal Kwh { get; private set; }

    /// <summary>False when Zaptec gave no per-hour energy and the hours were spread evenly.</summary>
    public bool HasEnergyDetails { get; private set; }

    private readonly List<ChargingSessionHour> _hours = [];
    public IReadOnlyCollection<ChargingSessionHour> Hours => _hours.AsReadOnly();

    public ChargingSession(
        Guid id,
        Guid locationId,
        Guid zaptecSessionId,
        Guid chargerId,
        DateTime startUtc,
        DateTime endUtc,
        decimal kwh,
        bool hasEnergyDetails,
        IEnumerable<(DateTime HourStartUtc, decimal Kwh, bool Estimated)> hours)
        : base(id)
    {
        LocationId = locationId;
        ZaptecSessionId = zaptecSessionId;
        ChargerId = chargerId;
        StartUtc = startUtc;
        EndUtc = endUtc;
        Kwh = kwh;
        HasEnergyDetails = hasEnergyDetails;
        _hours.AddRange(hours.Select(h =>
            new ChargingSessionHour(Guid.NewGuid(), id, locationId, h.HourStartUtc, h.Kwh, h.Estimated)));
    }

    public ChargingSession() : base() { }
}
