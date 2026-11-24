namespace Domain.Common.Entities;

/// <summary>
/// One reading of a water heater, taken about once a minute: its power and, since spec 020, the litres of 40 °C water
/// in the tank. OSO gives only the values right now, so these samples are the heater's only history. They belong to
/// the location, not the connection, and outlive it. The litres are missing when OSO did not give them and for every
/// sample stored before spec 020.
/// </summary>
public class WaterHeaterPowerSample : Entity
{
    public Guid LocationId { get; private set; }
    public DateTime SampledAtUtc { get; private set; }
    public decimal PowerKw { get; private set; }
    public int? AvailableLitres40 { get; private set; }

    public WaterHeaterPowerSample(
        Guid id, Guid locationId, DateTime sampledAtUtc, decimal powerKw, int? availableLitres40 = null)
        : base(id)
    {
        LocationId = locationId;
        SampledAtUtc = sampledAtUtc;
        PowerKw = powerKw;
        AvailableLitres40 = availableLitres40;
    }

    public WaterHeaterPowerSample() : base() { }
}
