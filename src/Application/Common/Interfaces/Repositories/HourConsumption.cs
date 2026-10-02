namespace Application.Common.Interfaces.Repositories;

/// <summary>
/// Read-only projection of the <c>measurement_hour</c> TimescaleDB continuous aggregate.
/// Infrastructure maps this as a keyless EF Core view - never written to.
/// </summary>
public class HourConsumption
{
    public Guid LocationId { get; set; }
    public Guid MeterId { get; set; }
    public DateTime BucketStart { get; set; }
    public double AvgPowerWatts { get; set; }

    public decimal ConsumptionKwh => (decimal)AvgPowerWatts / 1_000m;
}
