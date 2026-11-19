namespace Domain.Common.Entities;

/// <summary>
/// One reading of a water heater's power, taken about once a minute. OSO gives only the power right now, so these
/// samples are the heater's only history. They belong to the location, not the connection, and outlive it.
/// </summary>
public class WaterHeaterPowerSample : Entity
{
    public Guid LocationId { get; private set; }
    public DateTime SampledAtUtc { get; private set; }
    public decimal PowerKw { get; private set; }

    public WaterHeaterPowerSample(Guid id, Guid locationId, DateTime sampledAtUtc, decimal powerKw)
        : base(id)
    {
        LocationId = locationId;
        SampledAtUtc = sampledAtUtc;
        PowerKw = powerKw;
    }

    public WaterHeaterPowerSample() : base() { }
}
