namespace Domain.Common.Entities;

public class Measurement : Entity
{
    public Guid LocationId { get; private set; }
    public Guid MeterId { get; private set; }
    public DateTime Timestamp { get; private set; }
    public int PowerWatts { get; private set; }

    public Measurement(Guid id, Guid locationId, Guid meterId, DateTime timestamp, int powerWatts)
        : base(id)
    {
        LocationId = locationId;
        MeterId = meterId;
        Timestamp = timestamp;
        PowerWatts = powerWatts;
    }

    public Measurement() : base() { }
}
