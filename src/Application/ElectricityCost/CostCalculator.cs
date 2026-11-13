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
    INorgesPrisRateRepository norgesPrisRates)
{
    private const string EurNok = "EURNOK";
    private const int MaxRateLookbackDays = 7;

    // The calculator is scoped, so the (tiny) rate list is read once per request, however many hours are priced.
    private Task<IReadOnlyList<NorgesPrisRate>>? _rates;

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
            ? await GetNorgesPrisRateAsync(hourStartUtc, ct)
            : await GetSpotRateAsync(location.Zone, hourStartUtc, ct);

        return rate is null ? null : new RateCost(rate.Value, consumptionKwh * rate.Value);
    }

    private async Task<decimal?> GetNorgesPrisRateAsync(DateTime hourStartUtc, CancellationToken ct)
    {
        _rates ??= norgesPrisRates.GetAllAsync(ct);
        NorgesPrisRate? rate = NorgesPrisSchedule.RateFor(await _rates, hourStartUtc);
        return rate is null ? null : rate.RatePerKwh + rate.TaxPerKwh;
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
