using System.Globalization;
using Application.Common.Interfaces.Repositories;
using Application.ElectricityCost;
using Domain.Common.Entities;
using Features.Tests.NorgesPris;
using NSubstitute;

namespace Features.Tests.Pricing;

/// <summary>
/// Whole-hour prices through the calculator, checked against values worked out by hand. Spot is 100 EUR/MWh at 10 NOK
/// per EUR, so the spot energy part is exactly 1.00 NOK per kWh. The 2026 national values are VAT 25 percent,
/// electricity tax 0.0713 and Enova fee 0.0100 (taxes 0.0813).
/// </summary>
public class CostCalculatorPriceTests
{
    private static readonly Guid Tariff = Guid.NewGuid();

    // Wednesday 2026-12-02, 14:00 Oslo (UTC+1): a weekday day hour. Sunday 2026-12-06, 14:00 Oslo: a night hour.
    private static readonly DateTime WeekdayAfternoon = Utc("2026-12-02T13:00:00Z");
    private static readonly DateTime SundayAfternoon = Utc("2026-12-06T13:00:00Z");

    private readonly IElectricityPriceRepository<ElectricityPrice> _prices =
        Substitute.For<IElectricityPriceRepository<ElectricityPrice>>();

    private readonly IExchangeRateRepository<ExchangeRate> _fx =
        Substitute.For<IExchangeRateRepository<ExchangeRate>>();

    private static DateTime Utc(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    public CostCalculatorPriceTests()
    {
        _prices.GetByRegionAndHourAsync("NO1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => new ElectricityPrice(Guid.NewGuid(), "NO1", call.ArgAt<DateTime>(1), 100m));
        _fx.GetByDateAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => new ExchangeRate(Guid.NewGuid(), "EURNOK", call.ArgAt<DateOnly>(1), 10m));
    }

    private static Location LocationWith(bool norgesPris, Guid? tariff, decimal markup)
    {
        var location = new Location(Guid.NewGuid(), "Home", "Addr", "SN1", "NO1", true, norgesPris);
        location.SetPricing(tariff, markup, false, false, false);
        return location;
    }

    private static IPriceRateRepository FullRates(string gridFrom = "2026-01-01") => PriceRateStubs.Repository(
        PriceRateStubs.National2026(),
        [PriceRateStubs.Grid(Tariff, gridFrom, 0.30m, 0.20m)]);

    private CostCalculator Calculator(
        IPriceRateRepository? rates = null, INorgesPrisRateRepository? norgesPris = null) =>
        new(_prices, _fx, norgesPris ?? NorgesPrisRates.Seeded(), rates ?? FullRates());

    [Fact]
    public async Task ASpotWeekdayDayHour_IsTheFullPriceByHand()
    {
        // (1.00 + 0.02 + 0.30 + 0.0813) x 1.25 = 1.751625
        CostResult result = await Calculator().CalculateAsync(
            LocationWith(false, Tariff, 0.02m), WeekdayAfternoon, 2m, 0m, CancellationToken.None);

        Assert.Equal(PricingModel.Spot, result.PricingModel);
        Assert.Equal(1.751625m, result.Actual!.RatePerKwh);
        Assert.Equal(3.503250m, result.Actual.Cost);
        Assert.Equal(0.30m, result.Actual.Breakdown.Grid);
    }

    [Fact]
    public async Task ASundayHour_UsesTheNightAndWeekendRate_SoItDiffersByTheRateGapPlusVat()
    {
        Location location = LocationWith(false, Tariff, 0.02m);
        CostCalculator calculator = Calculator();

        CostResult weekday = await calculator.CalculateAsync(location, WeekdayAfternoon, 1m, 0m, CancellationToken.None);
        CostResult sunday = await calculator.CalculateAsync(location, SundayAfternoon, 1m, 0m, CancellationToken.None);

        // (1.00 + 0.02 + 0.20 + 0.0813) x 1.25 = 1.626625; the gap is (0.30 - 0.20) x 1.25.
        Assert.Equal(1.626625m, sunday.Actual!.RatePerKwh);
        Assert.Equal(0.125m, weekday.Actual!.RatePerKwh - sunday.Actual.RatePerKwh);
        Assert.Equal(0.20m, sunday.Actual.Breakdown.Grid);
    }

    [Fact]
    public async Task ANorgesPrisHour_HasTheMarkupAndTheNorgesPrisRateAsEnergy()
    {
        // The supplier charges its markup under Norgespris too: (0.40 + 0.02 + 0.30 + 0.0813) x 1.25 = 1.001625
        CostResult result = await Calculator().CalculateAsync(
            LocationWith(true, Tariff, 0.02m), WeekdayAfternoon, 1m, 0m, CancellationToken.None);

        Assert.Equal(PricingModel.NorgesPris, result.PricingModel);
        Assert.Equal(1.001625m, result.Actual!.RatePerKwh);
        Assert.Equal(0.02m, result.Actual.Breakdown.Markup);
        Assert.Equal(0.40m, result.Actual.Breakdown.Energy);
    }

    [Fact]
    public async Task TheComparison_DiffersOnlyInTheEnergy()
    {
        CostResult result = await Calculator().CalculateAsync(
            LocationWith(true, Tariff, 0.02m), WeekdayAfternoon, 1m, 0m, CancellationToken.None);

        PriceBreakdown actual = result.Actual!.Breakdown; // Norgespris terms
        PriceBreakdown comparison = result.Comparison!.Breakdown; // spot terms
        Assert.Equal(1.751625m, result.Comparison.RatePerKwh);
        Assert.Equal(1.00m, comparison.Energy);
        Assert.Equal(0.02m, comparison.Markup);
        Assert.Equal(actual.Markup, comparison.Markup);
        Assert.Equal(actual.Grid, comparison.Grid);
        Assert.Equal(actual.Taxes, comparison.Taxes);
    }

    [Fact]
    public async Task ASpotLocationComparison_IsNorgesPrisTermsWithTheMarkup()
    {
        CostResult result = await Calculator().CalculateAsync(
            LocationWith(false, Tariff, 0.02m), WeekdayAfternoon, 1m, 0m, CancellationToken.None);

        Assert.Equal(1.001625m, result.Comparison!.RatePerKwh);
        Assert.Equal(0.02m, result.Comparison.Breakdown.Markup);
    }

    [Fact]
    public async Task ANorgesPrisLocationWithNoMarkup_KeepsTheNorgesPrisPriceWithoutIt()
    {
        // A supplier without a markup under Norgespris is a markup of 0: (0.40 + 0.30 + 0.0813) x 1.25 = 0.976625
        CostResult result = await Calculator().CalculateAsync(
            LocationWith(true, Tariff, 0m), WeekdayAfternoon, 1m, 0m, CancellationToken.None);

        Assert.Equal(0.976625m, result.Actual!.RatePerKwh);
        Assert.Equal(0m, result.Actual.Breakdown.Markup);
    }

    [Fact]
    public async Task TheOldTaxFieldOfANorgesPrisRow_IsNotUsed()
    {
        INorgesPrisRateRepository withTax = NorgesPrisRates.Repository(NorgesPrisRates.Rate("2020-01-01", 0.40m, 0.05m));
        INorgesPrisRateRepository withoutTax = NorgesPrisRates.Repository(NorgesPrisRates.Rate("2020-01-01", 0.40m));
        Location location = LocationWith(true, Tariff, 0m);

        CostResult a = await Calculator(norgesPris: withTax).CalculateAsync(
            location, WeekdayAfternoon, 1m, 0m, CancellationToken.None);
        CostResult b = await Calculator(norgesPris: withoutTax).CalculateAsync(
            location, WeekdayAfternoon, 1m, 0m, CancellationToken.None);

        Assert.Equal(b.Actual!.RatePerKwh, a.Actual!.RatePerKwh);
        Assert.Equal(0.976625m, a.Actual.RatePerKwh);
    }

    [Fact]
    public async Task ThePartsAddUpToTheTotal()
    {
        CostResult result = await Calculator().CalculateAsync(
            LocationWith(false, Tariff, 0.0123m), WeekdayAfternoon, 1m, 0m, CancellationToken.None);

        PriceBreakdown p = result.Actual!.Breakdown;
        Assert.Equal(p.Total, p.Energy + p.Markup + (p.Grid ?? 0m) + p.Taxes + p.Vat);
        Assert.Equal(p.Total, result.Actual.RatePerKwh);
    }

    [Fact]
    public async Task OnAClockChangeDay_EveryRealHourIsPricedWithTheNightRate()
    {
        Location location = LocationWith(false, Tariff, 0m);
        CostCalculator calculator = Calculator();
        DateTime start = Utc("2027-03-27T23:00:00Z"); // Sunday 28 March 2027, 23 hours

        for (int i = 0; i < 23; i++)
        {
            CostResult result = await calculator.CalculateAsync(location, start.AddHours(i), 1m, 0m, CancellationToken.None);
            Assert.Equal(0.20m, result.Actual!.Breakdown.Grid);
        }
    }

    [Fact]
    public async Task AnHourBeforeTheEarliestTaxRow_CannotBePriced_NotPricedAsZero()
    {
        // The national rows start in 2026; a missing tax must not be hidden.
        CostResult result = await Calculator().CalculateAsync(
            LocationWith(false, Tariff, 0m), Utc("2025-12-02T13:00:00Z"), 1m, 0m, CancellationToken.None);

        Assert.Null(result.Actual);
        Assert.Null(result.Comparison);
    }

    [Fact]
    public async Task AMissingSpotPrice_MakesTheSpotSideUnavailable_ButNotTheNorgesPrisSide()
    {
        _prices.GetByRegionAndHourAsync("NO1", WeekdayAfternoon, Arg.Any<CancellationToken>())
            .Returns((ElectricityPrice?)null);

        CostResult result = await Calculator().CalculateAsync(
            LocationWith(true, Tariff, 0m), WeekdayAfternoon, 1m, 0m, CancellationToken.None);

        Assert.NotNull(result.Actual); // Norgespris terms
        Assert.Null(result.Comparison); // spot terms
    }

    [Fact]
    public async Task TheFallbackSpotPrice_IsUsedOnlyForAnHourWithoutAPrice()
    {
        _prices.GetByRegionAndHourAsync("NO1", WeekdayAfternoon, Arg.Any<CancellationToken>())
            .Returns((ElectricityPrice?)null);
        Location location = LocationWith(false, Tariff, 0.02m);
        CostCalculator calculator = Calculator();

        CostResult missing = await calculator.CalculateAsync(
            location, WeekdayAfternoon, 1m, 0m, CancellationToken.None, fallbackSpotEnergy: 0.50m);
        CostResult published = await calculator.CalculateAsync(
            location, SundayAfternoon, 1m, 0m, CancellationToken.None, fallbackSpotEnergy: 0.50m);
        CostResult neither = await calculator.CalculateAsync(location, WeekdayAfternoon, 1m, 0m, CancellationToken.None);

        Assert.Equal(0.50m, missing.Actual!.Breakdown.Energy);
        Assert.Equal(1.00m, published.Actual!.Breakdown.Energy); // the published price wins
        Assert.Null(neither.Actual);
    }

    [Fact]
    public async Task SpotEnergy_IsTheSpotPriceAtTheExchangeRate()
    {
        Location location = LocationWith(false, Tariff, 0m);

        Assert.Equal(1.00m, await Calculator().SpotEnergyAsync(location, WeekdayAfternoon, CancellationToken.None));
    }

    [Fact]
    public async Task AMissingExchangeRate_MakesTheSpotSideUnavailable()
    {
        _fx.GetByDateAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns((ExchangeRate?)null);

        CostResult result = await Calculator().CalculateAsync(
            LocationWith(false, Tariff, 0m), WeekdayAfternoon, 1m, 0m, CancellationToken.None);

        Assert.Null(result.Actual);
        Assert.NotNull(result.Comparison);
    }

    [Fact]
    public async Task ALocationWithoutATariff_StillPrices_WithTheGridPartLeftOutAndMarkedNotSet()
    {
        // (1.00 + 0.02 + 0.0813) x 1.25 = 1.376625
        CostResult result = await Calculator().CalculateAsync(
            LocationWith(false, null, 0.02m), WeekdayAfternoon, 1m, 0m, CancellationToken.None);

        Assert.Equal(1.376625m, result.Actual!.RatePerKwh);
        Assert.Null(result.Actual.Breakdown.Grid);
        Assert.False(result.Actual.Breakdown.GridSet);
    }

    [Fact]
    public async Task ATariffWithNoRateForTheHourYet_BehavesLikeNoTariff()
    {
        CostResult result = await Calculator(FullRates(gridFrom: "2027-01-01")).CalculateAsync(
            LocationWith(false, Tariff, 0.02m), WeekdayAfternoon, 1m, 0m, CancellationToken.None);

        Assert.Null(result.Actual!.Breakdown.Grid);
        Assert.False(result.Actual.Breakdown.GridSet);
    }

    [Fact]
    public async Task OnceTheTariffIsSet_TheGridPartAppears()
    {
        Location location = LocationWith(false, null, 0.02m);
        CostCalculator calculator = Calculator();

        CostResult before = await calculator.CalculateAsync(location, WeekdayAfternoon, 1m, 0m, CancellationToken.None);
        location.SetPricing(Tariff, 0.02m, false, false, false);
        CostResult after = await calculator.CalculateAsync(location, WeekdayAfternoon, 1m, 0m, CancellationToken.None);

        Assert.False(before.Actual!.Breakdown.GridSet);
        Assert.True(after.Actual!.Breakdown.GridSet);
        Assert.Equal(0.375m, after.Actual.RatePerKwh - before.Actual.RatePerKwh); // 0.30 x 1.25
    }

    [Fact]
    public async Task PricingAMonthOfHours_LoadsTheDatedRatesOnce()
    {
        IPriceRateRepository rates = FullRates();
        INorgesPrisRateRepository norgesPris = NorgesPrisRates.Seeded();
        CostCalculator calculator = Calculator(rates, norgesPris);
        Location location = LocationWith(true, Tariff, 0.02m);

        for (int hour = 0; hour < 24 * 30; hour++)
        {
            await calculator.CalculateAsync(location, WeekdayAfternoon.AddHours(hour), 1m, 0m, CancellationToken.None);
        }

        await rates.Received(1).GetPriceRatesAsync(Arg.Any<CancellationToken>());
        await rates.Received(1).GetGridTariffRatesAsync(Arg.Any<CancellationToken>());
        await norgesPris.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterPreload_TheHoursOfTheRangeAreNotQueriedOneByOne()
    {
        DateTime from = Utc("2026-12-02T00:00:00Z");
        DateTime to = Utc("2026-12-03T00:00:00Z");
        IElectricityPriceRepository<ElectricityPrice> prices = Substitute.For<IElectricityPriceRepository<ElectricityPrice>>();
        IExchangeRateRepository<ExchangeRate> fx = Substitute.For<IExchangeRateRepository<ExchangeRate>>();
        prices.GetByRegionAndHourRangeAsync("NO1", from, to, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, 24)
                .Select(h => new ElectricityPrice(Guid.NewGuid(), "NO1", from.AddHours(h), 100m))
                .ToList());
        fx.GetByDateRangeAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns([new ExchangeRate(Guid.NewGuid(), "EURNOK", new DateOnly(2026, 12, 1), 10m)]);
        var calculator = new CostCalculator(prices, fx, NorgesPrisRates.Seeded(), FullRates());
        Location location = LocationWith(false, Tariff, 0m);

        await calculator.PreloadAsync(location, from, to, CancellationToken.None);
        CostResult result = await calculator.CalculateAsync(location, from.AddHours(13), 1m, 0m, CancellationToken.None);

        // 2026-12-02 has no rate of its own: the look-back finds 2026-12-01 in the preloaded rows.
        // (1.00 + 0 + 0.30 + 0.0813) x 1.25 = 1.726625
        Assert.Equal(1.726625m, result.Actual!.RatePerKwh);
        await prices.DidNotReceive().GetByRegionAndHourAsync(
            Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await fx.DidNotReceive().GetByDateAsync(
            Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        await prices.Received(1).GetByRegionAndHourRangeAsync(
            "NO1", from, to, Arg.Any<CancellationToken>());
        await fx.Received(1).GetByDateRangeAsync(
            "EURNOK", Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnHourOutsideThePreloadedRange_FallsBackToTheSingleRowQuery()
    {
        DateTime from = Utc("2026-12-02T00:00:00Z");
        IElectricityPriceRepository<ElectricityPrice> prices = Substitute.For<IElectricityPriceRepository<ElectricityPrice>>();
        IExchangeRateRepository<ExchangeRate> fx = Substitute.For<IExchangeRateRepository<ExchangeRate>>();
        prices.GetByRegionAndHourRangeAsync("NO1", from, from.AddHours(1), Arg.Any<CancellationToken>())
            .Returns(new List<ElectricityPrice>());
        fx.GetByDateRangeAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<ExchangeRate>());
        prices.GetByRegionAndHourAsync("NO1", from.AddDays(18), Arg.Any<CancellationToken>())
            .Returns(new ElectricityPrice(Guid.NewGuid(), "NO1", from.AddDays(18), 100m));
        fx.GetByDateAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => new ExchangeRate(Guid.NewGuid(), "EURNOK", call.ArgAt<DateOnly>(1), 10m));
        var calculator = new CostCalculator(prices, fx, NorgesPrisRates.Seeded(), FullRates());
        Location location = LocationWith(false, Tariff, 0m);

        await calculator.PreloadAsync(location, from, from.AddHours(1), CancellationToken.None);
        CostResult result = await calculator.CalculateAsync(location, from.AddDays(18), 1m, 0m, CancellationToken.None);

        Assert.NotNull(result.Actual);
    }
}
