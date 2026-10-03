namespace Domain.Common.Entities;

public class ElectricityPrice : Entity
{
    public string PriceRegion { get; private set; } = null!;
    public DateTime HourStartUtc { get; private set; }
    public decimal PriceEurPerMwh { get; private set; }

    public ElectricityPrice(Guid id, string priceRegion, DateTime hourStartUtc, decimal priceEurPerMwh)
        : base(id)
    {
        PriceRegion = priceRegion;
        HourStartUtc = hourStartUtc;
        PriceEurPerMwh = priceEurPerMwh;
    }

    public ElectricityPrice() : base() { }
}
