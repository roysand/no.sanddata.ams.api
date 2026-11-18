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
/// <param name="Breakdown">
/// The parts of the price; they add up exactly to the total. When part of the hour is above the Norgespris cap, these
/// are the terms of the kWh within the cap (or the spot terms when every kWh is above it).
/// </param>
/// <param name="Cap">Set on Norgespris terms when some kWh of the hour are above the monthly cap; null otherwise.</param>
public record RateCost(decimal RatePerKwh, decimal Cost, PriceBreakdown Breakdown, CapSplit? Cap = null);

/// <summary>
/// How an hour on Norgespris terms is split by the monthly cap. Cost = KwhWithinCap x the Norgespris total +
/// KwhAboveCap x <see cref="AboveCapBreakdown"/>.Total, exactly.
/// </summary>
public record CapSplit(decimal KwhWithinCap, decimal KwhAboveCap, PriceBreakdown AboveCapBreakdown);

public record CostResult(PricingModel PricingModel, RateCost? Actual, RateCost? Comparison);

/// <summary>
/// The one place an hour is priced: (1 + VAT) x (energy + supplier markup + grid energy charge + taxes), with the
/// exemptions of the location and the Norgespris monthly cap. Scoped, so the dated values are read once per request
/// however many hours are priced.
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

    /// <param name="monthKwhBefore">
    /// The kWh the location used in the same Oslo calendar month before this hour, so the Norgespris cap is applied
    /// the same way whatever range of hours is asked for.
    /// </param>
    public async Task<CostResult> CalculateAsync(
        Location location, DateTime hourStartUtc, decimal consumptionKwh, decimal monthKwhBefore, CancellationToken ct)
    {
        PricingModel enrolled = location.HasNorgesPriceAgreement ? PricingModel.NorgesPris : PricingModel.Spot;
        RateCost? actual = await CalculateForModelAsync(
            enrolled, location, hourStartUtc, consumptionKwh, monthKwhBefore, ct);

        // The comparison gets the same grid charge, taxes and VAT; only the energy and the markup differ.
        PricingModel other = enrolled == PricingModel.Spot ? PricingModel.NorgesPris : PricingModel.Spot;
        RateCost? comparison = await CalculateForModelAsync(
            other, location, hourStartUtc, consumptionKwh, monthKwhBefore, ct);

        return new CostResult(enrolled, actual, comparison);
    }

    private async Task<RateCost?> CalculateForModelAsync(
        PricingModel model,
        Location location,
        DateTime hourStartUtc,
        decimal consumptionKwh,
        decimal monthKwhBefore,
        CancellationToken ct)
    {
        if (model == PricingModel.Spot)
        {
            PriceBreakdown? spot = await TermsAsync(PricingModel.Spot, location, hourStartUtc, ct);
            return spot is null ? null : new RateCost(spot.Total, consumptionKwh * spot.Total, spot);
        }

        decimal? cap = await CapForAsync(location, hourStartUtc, ct);
        (decimal within, decimal above) = NorgesPrisCap.Split(monthKwhBefore, consumptionKwh, cap);

        // An hour with no consumption gets the rate of the next kWh.
        bool allAbove = within == 0m && (above > 0m || NorgesPrisCap.IsPassed(monthKwhBefore, cap));
        if (above == 0m && !allAbove)
        {
            PriceBreakdown? norgesPris = await TermsAsync(PricingModel.NorgesPris, location, hourStartUtc, ct);
            return norgesPris is null
                ? null
                : new RateCost(norgesPris.Total, consumptionKwh * norgesPris.Total, norgesPris);
        }

        // Above the cap the kWh are priced on spot terms, markup included.
        PriceBreakdown? aboveCap = await TermsAsync(PricingModel.Spot, location, hourStartUtc, ct);
        if (aboveCap is null)
        {
            return null;
        }

        if (allAbove)
        {
            return new RateCost(
                aboveCap.Total, consumptionKwh * aboveCap.Total, aboveCap, new CapSplit(0m, above, aboveCap));
        }

        PriceBreakdown? withinCap = await TermsAsync(PricingModel.NorgesPris, location, hourStartUtc, ct);
        if (withinCap is null)
        {
            return null;
        }

        decimal cost = within * withinCap.Total + above * aboveCap.Total;
        return new RateCost(cost / consumptionKwh, cost, withinCap, new CapSplit(within, above, aboveCap));
    }

    /// <summary>The price of one kWh on the given terms, or null when a part of it is missing.</summary>
    private async Task<PriceBreakdown?> TermsAsync(
        PricingModel model, Location location, DateTime hourStartUtc, CancellationToken ct)
    {
        decimal? energy = model == PricingModel.NorgesPris
            ? await GetNorgesPrisEnergyAsync(hourStartUtc, ct)
            : await GetSpotEnergyAsync(location.Zone, hourStartUtc, ct);
        if (energy is null)
        {
            return null;
        }

        // An exempt location does not need the value it is exempt from; the Enova fee applies everywhere.
        PriceRateBook book = await GetBookAsync(ct);
        decimal? electricityTax = location.ElectricityTaxExempt
            ? 0m
            : book.ValueFor(PriceRateKind.ElectricityTax, hourStartUtc);
        decimal? enovaFee = book.ValueFor(PriceRateKind.EnovaFee, hourStartUtc);
        decimal? vat = location.VatExempt ? 0m : book.ValueFor(PriceRateKind.Vat, hourStartUtc);
        if (electricityTax is null || enovaFee is null || vat is null)
        {
            // A gap in the national data must never be hidden as a zero.
            return null;
        }

        // The supplier charges its markup on every kWh, also under Norgespris (spec 010, user story 1).
        return PriceCalculation.Calculate(
            energy.Value,
            location.SupplierMarkupPerKwh,
            GridRate(book, location, hourStartUtc),
            electricityTax.Value,
            enovaFee.Value,
            vat.Value);
    }

    /// <summary>The cap of the location kind valid on the first Oslo day of the month of the hour, or null.</summary>
    private async Task<decimal?> CapForAsync(Location location, DateTime hourStartUtc, CancellationToken ct)
    {
        DateOnly date = NorgesPrisSchedule.OsloDateOf(hourStartUtc);
        PriceRateKind kind = location.IsHolidayHome
            ? PriceRateKind.NorgesPrisCapHolidayHome
            : PriceRateKind.NorgesPrisCapHome;
        return (await GetBookAsync(ct)).ValueOn(kind, new DateOnly(date.Year, date.Month, 1));
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
