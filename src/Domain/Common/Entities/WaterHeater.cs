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

    /// <summary>
    /// The last Oslo day the OSO subscription key is valid (spec 015). OSO doesn't report it, so an administrator enters
    /// it; the range is checked by the handlers, which know today's date.
    /// </summary>
    public DateOnly SubscriptionKeyExpiresOn { get; private set; }

    public WaterHeater(
        Guid id, Guid locationId, string deviceId, string protectedSubscriptionKey, DateOnly subscriptionKeyExpiresOn)
        : base(id)
    {
        LocationId = locationId;
        DeviceId = deviceId.ToLowerInvariant();
        ProtectedSubscriptionKey = protectedSubscriptionKey;
        SubscriptionKeyExpiresOn = subscriptionKeyExpiresOn;
    }

    public WaterHeater() : base() { }

    /// <summary>
    /// Replaces the device id and key, keeping when the heater was first connected. A new key brings its own expiry date.
    /// </summary>
    public void Replace(string deviceId, string protectedSubscriptionKey, DateOnly subscriptionKeyExpiresOn)
    {
        DeviceId = deviceId.ToLowerInvariant();
        ProtectedSubscriptionKey = protectedSubscriptionKey;
        SubscriptionKeyExpiresOn = subscriptionKeyExpiresOn;
    }

    /// <summary>Changes only the key's expiry date; the key and the device id stay as they are.</summary>
    public void SetKeyExpiry(DateOnly subscriptionKeyExpiresOn) => SubscriptionKeyExpiresOn = subscriptionKeyExpiresOn;

    /// <summary>Never prints the device id or the key.</summary>
    public override string ToString() => $"WaterHeater(LocationId={LocationId})";
}
