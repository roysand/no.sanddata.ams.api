namespace Domain.Common.Entities;

/// <summary>
/// The day rate and the night and weekend rate of a grid tariff from <see cref="ValidFrom"/>, in NOK per kWh excluding
/// taxes and VAT. Never edited; corrected by adding a newer row.
/// </summary>
public class GridTariffRate : Entity
{
    public Guid GridTariffId { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public decimal DayRatePerKwh { get; private set; }
    public decimal NightRatePerKwh { get; private set; }
    public Guid AddedByUserId { get; private set; }

    public GridTariffRate(
        Guid id, Guid gridTariffId, DateOnly validFrom, decimal dayRatePerKwh, decimal nightRatePerKwh,
        Guid addedByUserId)
        : base(id)
    {
        GridTariffId = gridTariffId;
        ValidFrom = validFrom;
        DayRatePerKwh = dayRatePerKwh;
        NightRatePerKwh = nightRatePerKwh;
        AddedByUserId = addedByUserId;
        CreatedAt = UpdatedAt = DateTime.UtcNow;
    }

    public GridTariffRate() : base() { }
}
