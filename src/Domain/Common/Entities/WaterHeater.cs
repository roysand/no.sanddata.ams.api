namespace Domain.Common.Entities;

/// <summary>
/// An OSO water heater connected to a location (at most one per location, one location per device).
/// The subscription key is held only in protected (encrypted) form; neither it nor the device id may be logged.
/// <see cref="Entity.CreatedAt"/> is when it was connected, <see cref="Entity.UpdatedAt"/> when it last changed.
/// </summary>
public class WaterHeater : Entity
{
    public Guid LocationId { get; private set; }
    public string DeviceId { get; private set; } = null!;
    public string ProtectedSubscriptionKey { get; private set; } = null!;

    public WaterHeater(Guid id, Guid locationId, string deviceId, string protectedSubscriptionKey)
        : base(id)
    {
        LocationId = locationId;
        DeviceId = deviceId.ToLowerInvariant();
        ProtectedSubscriptionKey = protectedSubscriptionKey;
    }

    public WaterHeater() : base() { }

    /// <summary>Replaces the device id and key, keeping when the heater was first connected.</summary>
    public void Replace(string deviceId, string protectedSubscriptionKey)
    {
        DeviceId = deviceId.ToLowerInvariant();
        ProtectedSubscriptionKey = protectedSubscriptionKey;
    }

    /// <summary>Never prints the device id or the key.</summary>
    public override string ToString() => $"WaterHeater(LocationId={LocationId})";
}
