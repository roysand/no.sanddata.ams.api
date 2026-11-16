using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;

namespace Application.ElectricityCost;

public enum PricingModel
{
    Spot,
    NorgesPris
}

/// <param name="RatePerKwh">The full price per kWh including VAT (the total of the breakdown).</param>
/// <param name="Cost">Consumption times <paramref name="RatePerKwh"/>.</param>
/// <param name="Breakdown">The parts of the price; they add up exactly to the total.</param>
public record RateCost(decimal RatePerKwh, decimal Cost, PriceBreakdown Breakdown);

public record CostResult(PricingModel PricingModel, RateCost? Actual, RateCost? Comparison);

/// <summary>
/// The one place an hour is priced: (1 + VAT) x (energy + supplier markup + grid energy charge + taxes). Scoped, so the
/// dated values are read once per request however many hours are priced.
/// </summary>
public class CostCalculator(
    IElectricityPriceRepository<ElectricityPrice> priceRepository,
    IExchangeRateRepository<ExchangeRate> exchangeRateRepository,
    INorgesPrisRateRepository norgesPrisRates,
    IPriceRateRepository priceRates)
{
    private const string EurNok = "EURNOK";
    private const int MaxRateLookbackDays = 7;

    // Read once per instance (the calculator is scoped, so once per request).
    private Task<IReadOnlyList<NorgesPrisRate>>? _norgesPris;
    private Task<PriceRateBook>? _book;

    // Filled by PreloadAsync so a range of hours does not cost one query per hour.
    private (string Zone, DateTime From, DateTime To, Dictionary<DateTime, ElectricityPrice> Prices)? _preloadedPrices;
    private (DateOnly From, DateOnly To, Dictionary<DateOnly, ExchangeRate> Rates)? _preloadedExchangeRates;

    /// <summary>
    /// Loads the spot prices of the location zone and the exchange rates for the hours from
    /// <paramref name="fromUtc"/> (inclusive) to <paramref name="toUtc"/> (exclusive), so pricing the hours in between
    /// does not query once per hour. Hours outside the range still work, with a query each.
    /// </summary>
    public async Task PreloadAsync(Location location, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        IReadOnlyList<ElectricityPrice> prices =
            await priceRepository.GetByRegionAndHourRangeAsync(location.Zone, fromUtc, toUtc, ct);
        _preloadedPrices = (location.Zone, fromUtc, toUtc, prices.ToDictionary(p => p.HourStartUtc));

        // The look-back covers weekends and holidays, when Norges Bank publishes no rate.
        DateOnly from = DateOnly.FromDateTime(fromUtc).AddDays(-MaxRateLookbackDays);
        var to = DateOnly.FromDateTime(toUtc);
        IReadOnlyList<ExchangeRate> rates = await exchangeRateRepository.GetByDateRangeAsync(EurNok, from, to, ct);
        _preloadedExchangeRates = (from, to, rates.ToDictionary(r => r.RateDate));
    }

    public async Task<CostResult> CalculateAsync(
        Location location, DateTime hourStartUtc, decimal consumptionKwh, CancellationToken ct)
    {
        PricingModel enrolled = location.HasNorgesPriceAgreement ? PricingModel.NorgesPris : PricingModel.Spot;
        RateCost? actual = await CalculateForModelAsync(enrolled, location, hourStartUtc, consumptionKwh, ct);

        // The comparison gets the same grid charge, taxes and VAT; only the energy and the markup differ.
        PricingModel other = enrolled == PricingModel.Spot ? PricingModel.NorgesPris : PricingModel.Spot;
        RateCost? comparison = await CalculateForModelAsync(other, location, hourStartUtc, consumptionKwh, ct);

        return new CostResult(enrolled, actual, comparison);
    }

    private async Task<RateCost?> CalculateForModelAsync(
        PricingModel model, Location location, DateTime hourStartUtc, decimal consumptionKwh, CancellationToken ct)
    {
        decimal? energy = model == PricingModel.NorgesPris
            ? await GetNorgesPrisEnergyAsync(hourStartUtc, ct)
            : await GetSpotEnergyAsync(location.Zone, hourStartUtc, ct);
        if (energy is null)
        {
            return null;
        }

        PriceRateBook book = await GetBookAsync(ct);
        decimal? electricityTax = book.ValueFor(PriceRateKind.ElectricityTax, hourStartUtc);
        decimal? enovaFee = book.ValueFor(PriceRateKind.EnovaFee, hourStartUtc);
        decimal? vat = book.ValueFor(PriceRateKind.Vat, hourStartUtc);
        if (electricityTax is null || enovaFee is null || vat is null)
        {
            // A gap in the national data must never be hidden as a zero.
            return null;
        }

        // No supplier markup under Norgespris.
        decimal markup = model == PricingModel.Spot ? location.SupplierMarkupPerKwh : 0m;

        PriceBreakdown breakdown = PriceCalculation.Calculate(
            energy.Value, markup, GridRate(book, location, hourStartUtc), electricityTax.Value, enovaFee.Value, vat.Value);

        return new RateCost(breakdown.Total, consumptionKwh * breakdown.Total, breakdown);
    }

    /// <summary>The grid energy charge for the hour, or null when the location has no tariff or it has no rate yet.</summary>
    private static decimal? GridRate(PriceRateBook book, Location location, DateTime hourStartUtc)
    {
        if (location.GridTariffId is not { } tariffId || book.GridRateFor(tariffId, hourStartUtc) is not { } rate)
        {
            return null;
        }

        return GridPeriods.For(hourStartUtc) == GridPeriod.Day ? rate.DayRatePerKwh : rate.NightRatePerKwh;
    }

    private Task<PriceRateBook> GetBookAsync(CancellationToken ct) => _book ??= LoadBookAsync(ct);

    private async Task<PriceRateBook> LoadBookAsync(CancellationToken ct) =>
        new(await priceRates.GetPriceRatesAsync(ct), await priceRates.GetGridTariffRatesAsync(ct));

    /// <summary>
    /// The Norgespris rate for the hour. Only the rate counts: the old tax field of the row is not used, because the
    /// electricity tax and the Enova fee are added separately.
    /// </summary>
    private async Task<decimal?> GetNorgesPrisEnergyAsync(DateTime hourStartUtc, CancellationToken ct)
    {
        _norgesPris ??= norgesPrisRates.GetAllAsync(ct);
        NorgesPrisRate? rate = NorgesPrisSchedule.RateFor(await _norgesPris, hourStartUtc);
        return rate?.RatePerKwh;
    }

    private async Task<decimal?> GetSpotEnergyAsync(string priceRegion, DateTime hourStartUtc, CancellationToken ct)
    {
        ElectricityPrice? price = await GetPriceAsync(priceRegion, hourStartUtc, ct);
        if (price is null)
        {
            return null;
        }

        // Norges Bank publishes business days only - fall back to the most recent earlier rate.
        var date = DateOnly.FromDateTime(hourStartUtc);
        for (int i = 0; i <= MaxRateLookbackDays; i++)
        {
            ExchangeRate? fx = await GetExchangeRateAsync(date.AddDays(-i), ct);
            if (fx is not null)
            {
                return price.PriceEurPerMwh / 1000m * fx.Rate;
            }
        }

        return null;
    }

    private async Task<ElectricityPrice?> GetPriceAsync(string priceRegion, DateTime hourStartUtc, CancellationToken ct)
    {
        if (_preloadedPrices is { } loaded
            && loaded.Zone == priceRegion && hourStartUtc >= loaded.From && hourStartUtc < loaded.To)
        {
            return loaded.Prices.GetValueOrDefault(hourStartUtc);
        }

        return await priceRepository.GetByRegionAndHourAsync(priceRegion, hourStartUtc, ct);
    }

    private async Task<ExchangeRate?> GetExchangeRateAsync(DateOnly date, CancellationToken ct)
    {
        if (_preloadedExchangeRates is { } loaded && date >= loaded.From && date <= loaded.To)
        {
            return loaded.Rates.GetValueOrDefault(date);
        }

        return await exchangeRateRepository.GetByDateAsync(EurNok, date, ct);
    }
}
