namespace Application.Common.Interfaces.External;

/// <summary>Fetches daily currency exchange rates (Norges Bank open data API).</summary>
public interface IExchangeRateClient
{
    /// <summary>Returns null if no rate has been published for that date yet.</summary>
    Task<decimal?> GetRateAsync(DateOnly date, CancellationToken cancellationToken);
}
