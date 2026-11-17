namespace Domain.Common.Entities;

/// <summary>
/// One step of the capacity charge (kapasitetsledd) of a grid tariff from <see cref="ValidFrom"/>: the monthly amount
/// for an average peak from <see cref="LowerKw"/> up to but not including <see cref="UpperKw"/> (null for the top step).
/// The steps of one tariff and start date form a table that is added and deleted as a whole; never edited.
/// </summary>
public class GridCapacityStep : Entity
{
    public Guid GridTariffId { get; private set; }
    public DateOnly ValidFrom { get; private set; }
    public decimal LowerKw { get; private set; }
    public decimal? UpperKw { get; private set; }

    /// <summary>NOK per month excluding VAT.</summary>
    public decimal MonthlyAmount { get; private set; }

    public Guid AddedByUserId { get; private set; }

    public GridCapacityStep(
        Guid id, Guid gridTariffId, DateOnly validFrom, decimal lowerKw, decimal? upperKw, decimal monthlyAmount,
        Guid addedByUserId)
        : base(id)
    {
        GridTariffId = gridTariffId;
        ValidFrom = validFrom;
        LowerKw = lowerKw;
        UpperKw = upperKw;
        MonthlyAmount = monthlyAmount;
        AddedByUserId = addedByUserId;
        CreatedAt = UpdatedAt = DateTime.UtcNow;
    }

    public GridCapacityStep() : base() { }
}
