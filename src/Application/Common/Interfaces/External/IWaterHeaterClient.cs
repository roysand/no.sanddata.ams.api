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

    /// <summary>
    /// The heater's power right now (kW) and the litres of 40 °C water in the tank, read in parallel (spec 020). Fails
    /// only when the power can't be read; litres that can't be read come back as null with their error.
    /// </summary>
    Task<Result<WaterHeaterSample>> GetSampleAsync(WaterHeaterCredentials credentials, CancellationToken cancellationToken);
}
