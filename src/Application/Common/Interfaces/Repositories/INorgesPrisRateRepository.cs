using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

/// <summary>
/// Declared on its own (not an <c>IRepository&lt;T&gt;</c>) so the immediately-saving <c>Delete</c> is not exposed:
/// a handler removes and saves once.
/// </summary>
public interface INorgesPrisRateRepository
{
    /// <summary>Every rate ordered by start date, oldest first. Read-only (not tracked).</summary>
    Task<IReadOnlyList<NorgesPrisRate>> GetAllAsync(CancellationToken cancellationToken);

    Task<NorgesPrisRate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> DateExistsAsync(DateOnly validFrom, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the rate. Returns false, and saves nothing, when another rate already starts on that date, including when
    /// a concurrent request won the race.
    /// </summary>
    Task<bool> TryInsertAsync(NorgesPrisRate rate, CancellationToken cancellationToken);

    void Remove(NorgesPrisRate rate);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
