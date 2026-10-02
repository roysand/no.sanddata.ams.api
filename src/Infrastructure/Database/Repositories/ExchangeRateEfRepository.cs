using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class ExchangeRateEfRepository : GenericEfRepository<ExchangeRate>, IExchangeRateRepository<ExchangeRate>
{
    public ExchangeRateEfRepository(ApplicationDbContext applicationDbContext) : base(applicationDbContext)
    {
    }

    public async Task<ExchangeRate?> GetByDateAsync(string currencyPair, DateOnly date, CancellationToken cancellationToken) =>
        await _context.ExchangeRate
            .FirstOrDefaultAsync(r => r.CurrencyPair == currencyPair && r.RateDate == date, cancellationToken);
}
