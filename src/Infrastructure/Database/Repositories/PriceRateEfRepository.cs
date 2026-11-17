using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class PriceRateEfRepository(ApplicationDbContext context) : IPriceRateRepository
{
    public async Task<IReadOnlyList<PriceRate>> GetPriceRatesAsync(CancellationToken cancellationToken) =>
        await context.PriceRate.AsNoTracking().OrderBy(r => r.ValidFrom).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GridTariffRate>> GetGridTariffRatesAsync(CancellationToken cancellationToken) =>
        await context.GridTariffRate.AsNoTracking().OrderBy(r => r.ValidFrom).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GridTariff>> GetGridTariffsAsync(CancellationToken cancellationToken) =>
        await context.GridTariff.AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken);

    public async Task<GridTariff?> GetGridTariffByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await context.GridTariff.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<bool> GridTariffNameExistsAsync(string name, CancellationToken cancellationToken)
    {
        string lowered = name.ToLower();
        return await context.GridTariff.AnyAsync(t => t.Name.ToLower() == lowered, cancellationToken);
    }

    public async Task<PriceRate?> GetPriceRateByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await context.PriceRate.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<GridTariffRate?> GetGridTariffRateByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await context.GridTariffRate.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<bool> TryInsertPriceRateAsync(PriceRate rate, CancellationToken cancellationToken)
    {
        context.PriceRate.Add(rate);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // The unique index on kind and start date rejected it when a concurrent request added the same date first.
            context.Entry(rate).State = EntityState.Detached;
            if (await context.PriceRate.AnyAsync(
                    r => r.Kind == rate.Kind && r.ValidFrom == rate.ValidFrom, cancellationToken))
            {
                return false;
            }

            throw;
        }
    }

    public async Task<bool> TryInsertGridTariffRateAsync(GridTariffRate rate, CancellationToken cancellationToken)
    {
        context.GridTariffRate.Add(rate);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            context.Entry(rate).State = EntityState.Detached;
            if (await context.GridTariffRate.AnyAsync(
                    r => r.GridTariffId == rate.GridTariffId && r.ValidFrom == rate.ValidFrom, cancellationToken))
            {
                return false;
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<GridCapacityStep>> GetCapacityStepsAsync(
        Guid gridTariffId, CancellationToken cancellationToken) =>
        await context.GridCapacityStep.AsNoTracking()
            .Where(s => s.GridTariffId == gridTariffId)
            .OrderBy(s => s.ValidFrom).ThenBy(s => s.LowerKw)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GridCapacityStep>> GetCapacityStepTableAsync(
        Guid gridTariffId, DateOnly validFrom, CancellationToken cancellationToken) =>
        await context.GridCapacityStep
            .Where(s => s.GridTariffId == gridTariffId && s.ValidFrom == validFrom)
            .ToListAsync(cancellationToken);

    public async Task<bool> TryInsertCapacityStepsAsync(
        IReadOnlyList<GridCapacityStep> steps, CancellationToken cancellationToken)
    {
        context.GridCapacityStep.AddRange(steps);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            foreach (GridCapacityStep step in steps)
            {
                context.Entry(step).State = EntityState.Detached;
            }

            GridCapacityStep first = steps[0];
            if (await context.GridCapacityStep.AnyAsync(
                    s => s.GridTariffId == first.GridTariffId && s.ValidFrom == first.ValidFrom, cancellationToken))
            {
                return false;
            }

            throw;
        }
    }

    public void RemoveCapacitySteps(IEnumerable<GridCapacityStep> steps) => context.GridCapacityStep.RemoveRange(steps);

    public void InsertGridTariff(GridTariff tariff) => context.GridTariff.Add(tariff);

    public void RemovePriceRate(PriceRate rate) => context.PriceRate.Remove(rate);

    public void RemoveGridTariffRate(GridTariffRate rate) => context.GridTariffRate.Remove(rate);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
