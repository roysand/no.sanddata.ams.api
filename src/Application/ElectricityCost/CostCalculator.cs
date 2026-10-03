using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;

namespace Application.ElectricityCost;

public enum PricingModel
{
    Spot,
    NorgesPris
}

public record RateCost(decimal RatePerKwh, decimal Cost);

public record CostResult(PricingModel PricingModel, RateCost? Actual, RateCost? Comparison);

public class CostCalculator(
    IElectricityPriceRepository<ElectricityPrice> priceRepository,
    IExchangeRateRepository<ExchangeRate> exchangeRateRepository,
    FlatRateOptions flatRate)
{
    private const string EurNok = "EURNOK";
    private const int MaxRateLookbackDays = 7;

    public async Task<CostResult> CalculateAsync(
        Location location, DateTime hourStartUtc, decimal consumptionKwh, CancellationToken ct)
    {
        PricingModel enrolled = location.HasNorgesPriceAgreement ? PricingModel.NorgesPris : PricingModel.Spot;
        RateCost? actual = await CalculateForModelAsync(enrolled, location, hourStartUtc, consumptionKwh, ct);

        PricingModel other = enrolled == PricingModel.Spot ? PricingModel.NorgesPris : PricingModel.Spot;
        RateCost? comparison = await CalculateForModelAsync(other, location, hourStartUtc, consumptionKwh, ct);

        return new CostResult(enrolled, actual, comparison);
    }

    private async Task<RateCost?> CalculateForModelAsync(
        PricingModel model, Location location, DateTime hourStartUtc, decimal consumptionKwh, CancellationToken ct)
    {
        decimal? rate = model == PricingModel.NorgesPris
            ? flatRate.RatePerKwh + flatRate.TaxPerKwh
            : await GetSpotRateAsync(location.Zone, hourStartUtc, ct);

        return rate is null ? null : new RateCost(rate.Value, consumptionKwh * rate.Value);
    }

    private async Task<decimal?> GetSpotRateAsync(string priceRegion, DateTime hourStartUtc, CancellationToken ct)
    {
        ElectricityPrice? price = await priceRepository.GetByRegionAndHourAsync(priceRegion, hourStartUtc, ct);
        if (price is null)
        {
            return null;
        }

        // Norges Bank publishes business days only - fall back to the most recent earlier rate.
        var date = DateOnly.FromDateTime(hourStartUtc);
        for (int i = 0; i <= MaxRateLookbackDays; i++)
        {
            ExchangeRate? fx = await exchangeRateRepository.GetByDateAsync(EurNok, date.AddDays(-i), ct);
            if (fx is not null)
            {
                return price.PriceEurPerMwh / 1000m * fx.Rate;
            }
        }

        return null;
    }
}
