using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

public interface IExchangeRateRepository<T> : IRepository<T> where T : class
{
    Task<ExchangeRate?> GetByDateAsync(string currencyPair, DateOnly date, CancellationToken cancellationToken);

    /// <summary>The rates from the first date to the last date inclusive, ordered by date. Read-only.</summary>
    Task<IReadOnlyList<ExchangeRate>> GetByDateRangeAsync(
        string currencyPair, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
