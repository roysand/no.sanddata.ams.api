using Application.WaterHeaters;
using Domain.Common;

namespace Application.Common.Interfaces.External;

/// <summary>
/// Reads an OSO water heater through OSO's inCharge API. Read only on purpose (spec 011, FR-015): there is no method
/// that changes the heater. Never throws for HTTP, timeout or parsing failures; they come back as
/// <see cref="WaterHeaterErrors"/> values.
/// </summary>
public interface IWaterHeaterClient
{
    /// <summary>Status, temperatures, power and stored energy, read in parallel.</summary>
    Task<Result<WaterHeaterReading>> GetStateAsync(WaterHeaterCredentials credentials, CancellationToken cancellationToken);

    /// <summary>The heater's power right now, kW.</summary>
    Task<Result<decimal>> GetActualLoadAsync(WaterHeaterCredentials credentials, CancellationToken cancellationToken);
}
