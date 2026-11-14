namespace Domain.Common.Entities;

/// <summary>
/// A government-set flat price (Norgespris) that applies from <see cref="ValidFrom"/> until a later-dated rate takes
/// over. National: not tied to a location or price zone. Never edited; corrected by adding a newer rate.
/// </summary>
public class NorgesPrisRate : Entity
{
    public DateOnly ValidFrom { get; private set; }
    public decimal RatePerKwh { get; private set; }
    public decimal TaxPerKwh { get; private set; }
    public Guid AddedByUserId { get; private set; }

    public NorgesPrisRate(Guid id, DateOnly validFrom, decimal ratePerKwh, decimal taxPerKwh, Guid addedByUserId)
        : base(id)
    {
        ValidFrom = validFrom;
        RatePerKwh = ratePerKwh;
        TaxPerKwh = taxPerKwh;
        AddedByUserId = addedByUserId;
        CreatedAt = UpdatedAt = DateTime.UtcNow;
    }

    public NorgesPrisRate() : base() { }
}
