namespace Application.Common.Interfaces.External;

public record SpotPrice(DateTime HourStartUtc, decimal PriceEurPerMwh);

/// <summary>Fetches day-ahead electricity spot prices (ENTSO-E Transparency Platform).</summary>
public interface ISpotPriceClient
{
    Task<IReadOnlyList<SpotPrice>> GetPricesAsync(
        string priceRegion, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken);
}
