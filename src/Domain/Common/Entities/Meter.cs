namespace Domain.Common.Entities;

public class Meter : Entity
{
    public Guid LocationId { get; private set; }
    public Location Location { get; private set; } = null!;
    public string DeviceId { get; private set; } = null!;
    public string? MeterId { get; private set; }
    public string? MeterType { get; private set; }
    public string? Comment { get; private set; }
    public bool IsActive { get; private set; }

    public Meter(Guid id, Guid locationId, string deviceId, string? comment, bool isActive)
        : base(id)
    {
        LocationId = locationId;
        DeviceId = deviceId;
        Comment = comment;
        IsActive = isActive;
    }

    public Meter() : base() { }

    /// <summary>The only thing about a registered meter that can be edited; device id and location are fixed.</summary>
    public void SetComment(string? comment) => Comment = comment;

    public void UpdateMeterIdentity(string? meterId, string? meterType)
    {
        MeterId ??= meterId;
        MeterType ??= meterType;
    }
}
