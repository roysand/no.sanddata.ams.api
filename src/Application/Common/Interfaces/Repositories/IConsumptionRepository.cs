namespace Application.Common.Interfaces.Repositories;

/// <summary>
/// Read-only queries over the minute/hour consumption continuous aggregates. Deliberately does
/// not extend <see cref="IRepository{T}"/> - there's no insert/update/delete concept for a
/// materialized view, so the generic CRUD interface doesn't fit.
/// </summary>
public interface IConsumptionRepository
{
    Task<IReadOnlyList<MinuteConsumption>> GetMinuteAsync(
        Guid locationId, Guid? meterId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    Task<IReadOnlyList<HourConsumption>> GetHourlyAsync(
        Guid locationId, Guid? meterId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);

    Task<decimal> GetHourConsumptionKwhAsync(Guid locationId, DateTime hourStartUtc, CancellationToken cancellationToken);
}
