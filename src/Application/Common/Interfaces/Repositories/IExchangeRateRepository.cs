using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

public interface IExchangeRateRepository<T> : IRepository<T> where T : class
{
    Task<ExchangeRate?> GetByDateAsync(string currencyPair, DateOnly date, CancellationToken cancellationToken);
}
