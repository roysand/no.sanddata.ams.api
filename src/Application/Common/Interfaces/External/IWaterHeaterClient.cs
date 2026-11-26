using Application.WaterHeaters;
using Domain.Common;

namespace Application.Common.Interfaces.External;

/// <summary>
/// Reads an OSO water heater through OSO's inCharge API. Read only (spec 011, FR-015), except High Demand (spec 022,
/// FR-008): <see cref="SetHighDemandAsync"/> is the only method that changes the heater. Never throws for HTTP,
/// timeout or parsing failures; they come back as <see cref="WaterHeaterErrors"/> values.
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

    /// <summary>
    /// Turns High Demand on (<paramref name="on"/> true: OSO's undocumented <c>POST …/HighDemand/</c>) or off
    /// (<c>DELETE</c>), spec 022, research R1–R3. Sent once and never retried. OSO keeps it on for 24 hours. A 404
    /// gives <see cref="WaterHeaterErrors.HighDemandNotOffered"/>; other failures map as for the reads.
    /// </summary>
    Task<Result> SetHighDemandAsync(WaterHeaterCredentials credentials, bool on, CancellationToken cancellationToken);
}
