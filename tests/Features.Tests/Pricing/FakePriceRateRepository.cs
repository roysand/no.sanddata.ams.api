using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;

namespace Features.Tests.Pricing;

/// <summary>An in-memory rate store with the rules of the real one: ordered reads and unique start dates.</summary>
internal sealed class FakePriceRateRepository : IPriceRateRepository
{
    public List<PriceRate> PriceRates { get; } = [];

    public List<GridTariff> Tariffs { get; } = [];

    public List<GridTariffRate> TariffRates { get; } = [];

    public int SaveCalls { get; private set; }

    /// <summary>Set to simulate a concurrent request that added the same date after the handler check.</summary>
    public bool LoseTheRace { get; set; }

    public Task<IReadOnlyList<PriceRate>> GetPriceRatesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PriceRate>>(PriceRates.OrderBy(r => r.ValidFrom).ToList());

    public Task<IReadOnlyList<GridTariffRate>> GetGridTariffRatesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GridTariffRate>>(TariffRates.OrderBy(r => r.ValidFrom).ToList());

    public Task<IReadOnlyList<GridTariff>> GetGridTariffsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GridTariff>>(Tariffs.OrderBy(t => t.Name).ToList());

    public Task<GridTariff?> GetGridTariffByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Tariffs.FirstOrDefault(t => t.Id == id));

    public Task<bool> GridTariffNameExistsAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(Tariffs.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task<PriceRate?> GetPriceRateByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(PriceRates.FirstOrDefault(r => r.Id == id));

    public Task<GridTariffRate?> GetGridTariffRateByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(TariffRates.FirstOrDefault(r => r.Id == id));

    public Task<bool> TryInsertPriceRateAsync(PriceRate rate, CancellationToken cancellationToken)
    {
        if (LoseTheRace || PriceRates.Any(r => r.Kind == rate.Kind && r.ValidFrom == rate.ValidFrom))
        {
            return Task.FromResult(false);
        }

        PriceRates.Add(rate);
        SaveCalls++;
        return Task.FromResult(true);
    }

    public Task<bool> TryInsertGridTariffRateAsync(GridTariffRate rate, CancellationToken cancellationToken)
    {
        if (LoseTheRace || TariffRates.Any(r => r.GridTariffId == rate.GridTariffId && r.ValidFrom == rate.ValidFrom))
        {
            return Task.FromResult(false);
        }

        TariffRates.Add(rate);
        SaveCalls++;
        return Task.FromResult(true);
    }

    public List<GridCapacityStep> CapacitySteps { get; } = [];

    public Task<IReadOnlyList<GridCapacityStep>> GetCapacityStepsAsync(
        Guid gridTariffId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GridCapacityStep>>(CapacitySteps
            .Where(s => s.GridTariffId == gridTariffId)
            .OrderBy(s => s.ValidFrom).ThenBy(s => s.LowerKw)
            .ToList());

    public Task<IReadOnlyList<GridCapacityStep>> GetCapacityStepTableAsync(
        Guid gridTariffId, DateOnly validFrom, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GridCapacityStep>>(CapacitySteps
            .Where(s => s.GridTariffId == gridTariffId && s.ValidFrom == validFrom)
            .ToList());

    public Task<bool> TryInsertCapacityStepsAsync(
        IReadOnlyList<GridCapacityStep> steps, CancellationToken cancellationToken)
    {
        GridCapacityStep first = steps[0];
        if (LoseTheRace
            || CapacitySteps.Any(s => s.GridTariffId == first.GridTariffId && s.ValidFrom == first.ValidFrom))
        {
            return Task.FromResult(false);
        }

        CapacitySteps.AddRange(steps);
        SaveCalls++;
        return Task.FromResult(true);
    }

    public void RemoveCapacitySteps(IEnumerable<GridCapacityStep> steps)
    {
        foreach (GridCapacityStep step in steps.ToList())
        {
            CapacitySteps.Remove(step);
        }
    }

    public void InsertGridTariff(GridTariff tariff) => Tariffs.Add(tariff);

    public void RemovePriceRate(PriceRate rate) => PriceRates.Remove(rate);

    public void RemoveGridTariffRate(GridTariffRate rate) => TariffRates.Remove(rate);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCalls++;
        return Task.FromResult(1);
    }
}
