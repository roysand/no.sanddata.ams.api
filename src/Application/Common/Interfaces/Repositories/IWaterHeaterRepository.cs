using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

public interface IWaterHeaterRepository<T> : IRepository<T> where T : class
{
    /// <summary>The heater connected to the location, tracked; null when none is connected.</summary>
    Task<WaterHeater?> GetByLocationAsync(Guid locationId, CancellationToken cancellationToken);

    /// <summary>The heater with this device id (compared lowercased), tracked; null when none.</summary>
    Task<WaterHeater?> GetByDeviceIdAsync(string deviceId, CancellationToken cancellationToken);

    /// <summary>Every heater whose location is active. Read-only.</summary>
    Task<IReadOnlyList<WaterHeater>> GetForActiveLocationsAsync(CancellationToken cancellationToken);
}
