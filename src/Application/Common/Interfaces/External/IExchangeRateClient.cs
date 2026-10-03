using Domain.Common;

namespace Application.Common.Interfaces.External;

/// <summary>Fetches daily currency exchange rates (Norges Bank open data API).</summary>
public interface IExchangeRateClient
{
    /// <summary>Returns a NotFound failure if no rate has been published for that date yet; never throws.</summary>
    Task<Result<decimal>> GetRateAsync(DateOnly date, CancellationToken cancellationToken);
}
