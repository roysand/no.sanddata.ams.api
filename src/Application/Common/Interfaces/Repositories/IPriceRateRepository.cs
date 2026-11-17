using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

/// <summary>
/// The dated values the price is made of (national rates and grid tariffs). Declared on its own so a handler removes
/// and saves once; there is no immediately-saving delete.
/// </summary>
public interface IPriceRateRepository
{
    /// <summary>Every national rate ordered by start date. Read-only (not tracked).</summary>
    Task<IReadOnlyList<PriceRate>> GetPriceRatesAsync(CancellationToken cancellationToken);

    /// <summary>Every grid tariff rate ordered by start date. Read-only (not tracked).</summary>
    Task<IReadOnlyList<GridTariffRate>> GetGridTariffRatesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<GridTariff>> GetGridTariffsAsync(CancellationToken cancellationToken);

    Task<GridTariff?> GetGridTariffByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> GridTariffNameExistsAsync(string name, CancellationToken cancellationToken);

    Task<PriceRate?> GetPriceRateByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<GridTariffRate?> GetGridTariffRateByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the rate. Returns false, and saves nothing, when another row of the same kind already starts on that date,
    /// including when a concurrent request won the race.
    /// </summary>
    Task<bool> TryInsertPriceRateAsync(PriceRate rate, CancellationToken cancellationToken);

    /// <summary>As <see cref="TryInsertPriceRateAsync"/>, for a row of the same tariff.</summary>
    Task<bool> TryInsertGridTariffRateAsync(GridTariffRate rate, CancellationToken cancellationToken);

    /// <summary>Every capacity step of the tariff, of every start date. Read-only (not tracked).</summary>
    Task<IReadOnlyList<GridCapacityStep>> GetCapacityStepsAsync(Guid gridTariffId, CancellationToken cancellationToken);

    /// <summary>The steps of one table (tariff and start date), tracked so they can be removed.</summary>
    Task<IReadOnlyList<GridCapacityStep>> GetCapacityStepTableAsync(
        Guid gridTariffId, DateOnly validFrom, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the steps of one table together. Returns false, and saves nothing, when the tariff already has a table
    /// starting on that date, including when a concurrent request won the race.
    /// </summary>
    Task<bool> TryInsertCapacityStepsAsync(IReadOnlyList<GridCapacityStep> steps, CancellationToken cancellationToken);

    void RemoveCapacitySteps(IEnumerable<GridCapacityStep> steps);

    void InsertGridTariff(GridTariff tariff);

    void RemovePriceRate(PriceRate rate);

    void RemoveGridTariffRate(GridTariffRate rate);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
