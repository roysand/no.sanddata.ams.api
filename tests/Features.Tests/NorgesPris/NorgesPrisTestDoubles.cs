using System.Globalization;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;

namespace Features.Tests.NorgesPris;

/// <summary>A clock the test sets, so "today" can sit on either side of midnight in Oslo.</summary>
internal sealed class FixedTimeProvider(string utcNow) : TimeProvider
{
    private readonly DateTimeOffset _now = DateTimeOffset.Parse(utcNow, null, DateTimeStyles.AssumeUniversal);

    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>An in-memory rate store with the same rules as the real one: ordered reads, unique start dates.</summary>
internal sealed class FakeNorgesPrisRateRepository : INorgesPrisRateRepository
{
    public List<NorgesPrisRate> Stored { get; } = [];

    public int SaveCalls { get; private set; }

    /// <summary>Set to simulate a concurrent request that added the same date after the handler's check.</summary>
    public bool LoseTheRace { get; set; }

    public FakeNorgesPrisRateRepository(params NorgesPrisRate[] rates) => Stored.AddRange(rates);

    public Task<IReadOnlyList<NorgesPrisRate>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NorgesPrisRate>>(Stored.OrderBy(r => r.ValidFrom).ToList());

    public Task<NorgesPrisRate?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.FirstOrDefault(r => r.Id == id));

    public Task<bool> DateExistsAsync(DateOnly validFrom, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.Any(r => r.ValidFrom == validFrom));

    public Task<bool> TryInsertAsync(NorgesPrisRate rate, CancellationToken cancellationToken)
    {
        if (LoseTheRace || Stored.Any(r => r.ValidFrom == rate.ValidFrom))
        {
            return Task.FromResult(false);
        }

        Stored.Add(rate);
        SaveCalls++;
        return Task.FromResult(true);
    }

    public void Remove(NorgesPrisRate rate) => Stored.Remove(rate);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCalls++;
        return Task.FromResult(1);
    }
}
