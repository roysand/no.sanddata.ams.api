namespace Domain.Common.Entities;

/// <summary>
/// A national dated value (electricity tax and Enova fee in NOK per kWh, VAT as a fraction) that applies from
/// <see cref="ValidFrom"/> until a later-dated row of the same kind takes over. Never edited; corrected by adding a
/// newer row.
/// </summary>
public class PriceRate : Entity
{
    public PriceRateKind Kind { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public decimal Value { get; private set; }
    public Guid AddedByUserId { get; private set; }

    public PriceRate(Guid id, PriceRateKind kind, DateOnly validFrom, decimal value, Guid addedByUserId)
        : base(id)
    {
        Kind = kind;
        ValidFrom = validFrom;
        Value = value;
        AddedByUserId = addedByUserId;
        CreatedAt = UpdatedAt = DateTime.UtcNow;
    }

    public PriceRate() : base() { }
}
