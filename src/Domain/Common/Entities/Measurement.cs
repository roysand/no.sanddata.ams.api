namespace Domain.Common.Entities;

public class Measurement : Entity
{
    public Guid LocationId { get; private set; }
    public string DeviceId { get; private set; } = null!;
    public DateTime Timestamp { get; private set; }
    public string? MeterId { get; private set; }
    public string? MeterType { get; private set; }
    public int PowerWatts { get; private set; }

    public Measurement(
        Guid id,
        Guid locationId,
        string deviceId,
        DateTime timestamp,
        string? meterId,
        string? meterType,
        int powerWatts)
        : base(id)
    {
        LocationId = locationId;
        DeviceId = deviceId;
        Timestamp = timestamp;
        MeterId = meterId;
        MeterType = meterType;
        PowerWatts = powerWatts;
    }

    public Measurement() : base() { }
}
