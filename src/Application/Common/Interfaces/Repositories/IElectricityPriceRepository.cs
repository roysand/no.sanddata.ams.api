using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

public interface IElectricityPriceRepository<T> : IRepository<T> where T : class
{
    Task<ElectricityPrice?> GetByRegionAndHourAsync(string priceRegion, DateTime hourStartUtc, CancellationToken cancellationToken);

    Task<IReadOnlyList<ElectricityPrice>> GetByRegionAndHourRangeAsync(
        string priceRegion, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
}
