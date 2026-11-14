using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class NorgesPrisRateEfRepository(ApplicationDbContext context) : INorgesPrisRateRepository
{
    public async Task<IReadOnlyList<NorgesPrisRate>> GetAllAsync(CancellationToken cancellationToken) =>
        await context.NorgesPrisRate.AsNoTracking().OrderBy(r => r.ValidFrom).ToListAsync(cancellationToken);

    public async Task<NorgesPrisRate?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await context.NorgesPrisRate.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<bool> DateExistsAsync(DateOnly validFrom, CancellationToken cancellationToken) =>
        await context.NorgesPrisRate.AnyAsync(r => r.ValidFrom == validFrom, cancellationToken);

    public async Task<bool> TryInsertAsync(NorgesPrisRate rate, CancellationToken cancellationToken)
    {
        context.NorgesPrisRate.Add(rate);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // The unique index on ValidFrom rejected it when a concurrent request added the same date first.
            context.Entry(rate).State = EntityState.Detached;
            if (await DateExistsAsync(rate.ValidFrom, cancellationToken))
            {
                return false;
            }

            throw;
        }
    }

    public void Remove(NorgesPrisRate rate) => context.NorgesPrisRate.Remove(rate);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        context.SaveChangesAsync(cancellationToken);
}
