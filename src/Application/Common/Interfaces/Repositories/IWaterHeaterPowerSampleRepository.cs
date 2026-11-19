namespace Application.Common.Interfaces.Repositories;

/// <summary>Append-only store of water heater power samples.</summary>
public interface IWaterHeaterPowerSampleRepository<T> : IRepository<T> where T : class
{
}
