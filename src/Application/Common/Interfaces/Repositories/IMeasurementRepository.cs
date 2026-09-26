using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

public interface IMeasurementRepository<T> : IRepository<T> where T : class
{
    Task<(IReadOnlyList<Measurement> Items, int TotalCount)> GetPagedAsync(
        Guid locationId, Guid? meterId, DateTime from, DateTime to, int page, int pageSize,
        CancellationToken cancellationToken);

    Task<Measurement?> GetLatestAsync(Guid locationId, Guid? meterId, CancellationToken cancellationToken);
}
