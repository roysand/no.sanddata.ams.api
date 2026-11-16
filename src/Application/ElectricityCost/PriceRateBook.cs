using Domain.Common;
using Domain.Common.Entities;

namespace Application.ElectricityCost;

/// <summary>
/// The dated values the price is made of, loaded once. Each lookup returns the row with the latest start date on or
/// before the Oslo date of the hour, so past hours keep the values that applied then. Pure: no repositories.
/// </summary>
public class PriceRateBook
{
    private readonly IReadOnlyList<PriceRate> _rates;
    private readonly IReadOnlyList<GridTariffRate> _gridRates;

    /// <param name="rates">Every national rate.</param>
    /// <param name="gridRates">Every grid tariff rate.</param>
    public PriceRateBook(IReadOnlyList<PriceRate> rates, IReadOnlyList<GridTariffRate> gridRates)
    {
        // Sorted here so a lookup never depends on the caller order.
        _rates = rates.OrderBy(r => r.ValidFrom).ToList();
        _gridRates = gridRates.OrderBy(r => r.ValidFrom).ToList();
    }

    /// <summary>The value of the kind for the hour, or null when the hour is before the earliest row of that kind.</summary>
    public decimal? ValueFor(PriceRateKind kind, DateTime hourStartUtc)
    {
        DateOnly date = NorgesPrisSchedule.OsloDateOf(hourStartUtc);
        PriceRate? found = null;
        foreach (PriceRate rate in _rates)
        {
            if (rate.ValidFrom > date)
            {
                break;
            }

            if (rate.Kind == kind)
            {
                found = rate;
            }
        }

        return found?.Value;
    }

    /// <summary>The rate of the tariff for the hour, or null when the tariff has no row on or before that date.</summary>
    public GridTariffRate? GridRateFor(Guid gridTariffId, DateTime hourStartUtc)
    {
        DateOnly date = NorgesPrisSchedule.OsloDateOf(hourStartUtc);
        GridTariffRate? found = null;
        foreach (GridTariffRate rate in _gridRates)
        {
            if (rate.ValidFrom > date)
            {
                break;
            }

            if (rate.GridTariffId == gridTariffId)
            {
                found = rate;
            }
        }

        return found;
    }
}
