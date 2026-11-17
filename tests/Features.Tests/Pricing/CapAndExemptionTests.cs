using System.Globalization;
using Application.Common.Interfaces.Repositories;
using Application.ElectricityCost;
using Domain.Common;
using Domain.Common.Entities;
using Features.ElectricityCost.Handlers;
using Features.ElectricityCost.Queries;
using Features.ElectricityCost.Services;
using Features.Tests.NorgesPris;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Pricing;

/// <summary>
/// User story 6: the electricity-tax and VAT exemptions and the Norgespris monthly cap, against values worked out by
/// hand. Spot is 100 EUR/MWh at 10 NOK per EUR (1.00 NOK per kWh), Norgespris 0.40, markup 0.02, grid 0.30 day and
/// 0.20 night, electricity tax 0.0713, Enova fee 0.0100, VAT 25 percent. Per kWh, on a weekday day hour:
/// spot terms (1.00 + 0.02 + 0.30 + 0.0813) x 1.25 = 1.751625; Norgespris terms (0.40 + 0.30 + 0.0813) x 1.25 = 0.976625.
/// Caps: 5,000 kWh for a home, 1,000 kWh for a holiday home.
/// </summary>
public class CapAndExemptionTests
{
    private const decimal SpotTotal = 1.751625m;
    private const decimal NorgesPrisTotal = 0.976625m;

    private static readonly Guid Tariff = Guid.NewGuid();

    // Wednesday 2026-12-02, 14:00 Oslo (UTC+1): a weekday day hour.
    private static readonly DateTime WeekdayAfternoon = Utc("2026-12-02T13:00:00Z");

    private readonly IElectricityPriceRepository<ElectricityPrice> _prices =
        Substitute.For<IElectricityPriceRepository<ElectricityPrice>>();

    private readonly IExchangeRateRepository<ExchangeRate> _fx =
        Substitute.For<IExchangeRateRepository<ExchangeRate>>();

    public CapAndExemptionTests()
    {
        _prices.GetByRegionAndHourAsync("NO1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => new ElectricityPrice(Guid.NewGuid(), "NO1", call.ArgAt<DateTime>(1), 100m));
        _fx.GetByDateAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => new ExchangeRate(Guid.NewGuid(), "EURNOK", call.ArgAt<DateOnly>(1), 10m));
        _prices.ServeRangesFromSingleHours();
        _fx.ServeRangesFromSingleDates();
    }

    private static DateTime Utc(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private static Location LocationWith(
        bool norgesPris, bool taxExempt = false, bool vatExempt = false, bool holidayHome = false)
    {
        var location = new Location(Guid.NewGuid(), "Cabin", "Addr", "SN1", "NO1", true, norgesPris);
        location.SetPricing(Tariff, 0.02m, taxExempt, vatExempt, holidayHome);
        return location;
    }

    private static IEnumerable<PriceRate> Caps() =>
    [
        PriceRateStubs.Rate(PriceRateKind.NorgesPrisCapHome, "2025-10-01", 5000m),
        PriceRateStubs.Rate(PriceRateKind.NorgesPrisCapHolidayHome, "2025-10-01", 1000m)
    ];

    private static IPriceRateRepository Rates(IEnumerable<PriceRate>? extra = null) => PriceRateStubs.Repository(
        PriceRateStubs.National2026().Concat(Caps()).Concat(extra ?? []),
        [PriceRateStubs.Grid(Tariff, "2026-01-01", 0.30m, 0.20m)]);

    private CostCalculator Calculator(IPriceRateRepository? rates = null) =>
        new(_prices, _fx, NorgesPrisRates.Seeded(), rates ?? Rates());

    // ---- the split ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 5, null, 5, 0)]
    [InlineData(0, 5, 1000, 5, 0)]
    [InlineData(990, 20, 1000, 10, 10)]
    [InlineData(995, 5, 1000, 5, 0)]
    [InlineData(1000, 5, 1000, 0, 5)]
    [InlineData(1200, 5, 1000, 0, 5)]
    public void Split_PutsTheKwhUpToTheCapWithinIt(
        int before, int hour, int? cap, int expectedWithin, int expectedAbove)
    {
        (decimal within, decimal above) = NorgesPrisCap.Split(before, hour, cap);

        Assert.Equal(expectedWithin, within);
        Assert.Equal(expectedAbove, above);
    }

    // ---- exemptions --------------------------------------------------------------------------------------------

    [Fact]
    public async Task ATaxExemptLocation_PaysNoElectricityTax_ButTheEnovaFee()
    {
        // (1.00 + 0.02 + 0.30 + 0.0100) x 1.25 = 1.6625
        CostResult result = await Calculator().CalculateAsync(
            LocationWith(false, taxExempt: true), WeekdayAfternoon, 1m, 0m, CancellationToken.None);

        Assert.Equal(0.0100m, result.Actual!.Breakdown.Taxes);
        Assert.Equal(1.6625m, result.Actual.RatePerKwh);
    }

    [Fact]
    public async Task AVatExemptLocation_PaysNoVat()
    {
        // 1.00 + 0.02 + 0.30 + 0.0813 = 1.4013
        CostResult result = await Calculator().CalculateAsync(
            LocationWith(false, vatExempt: true), WeekdayAfternoon, 1m, 0m, CancellationToken.None);

        Assert.Equal(0m, result.Actual!.Breakdown.Vat);
        Assert.Equal(1.4013m, result.Actual.RatePerKwh);
    }

    [Fact]
    public async Task ALocationExemptFromBoth_PaysTheEnovaFeeOnly()
    {
        // 1.00 + 0.02 + 0.30 + 0.0100 = 1.33, and the same on the comparison side: 0.40 + 0.30 + 0.0100 = 0.71
        CostResult result = await Calculator().CalculateAsync(
            LocationWith(false, taxExempt: true, vatExempt: true), WeekdayAfternoon, 1m, 0m, CancellationToken.None);

        Assert.Equal(1.33m, result.Actual!.RatePerKwh);
        Assert.Equal(0.71m, result.Comparison!.RatePerKwh);
    }

    [Fact]
    public async Task AnExemptLocation_DoesNotNeedTheValueItIsExemptFrom()
    {
        IPriceRateRepository noTaxNoVat = PriceRateStubs.Repository(
            [PriceRateStubs.Rate(PriceRateKind.EnovaFee, "2026-01-01", 0.01m)],
            [PriceRateStubs.Grid(Tariff, "2026-01-01", 0.30m, 0.20m)]);

        CostResult exempt = await Calculator(noTaxNoVat).CalculateAsync(
            LocationWith(false, taxExempt: true, vatExempt: true), WeekdayAfternoon, 1m, 0m, CancellationToken.None);
        CostResult notExempt = await Calculator(noTaxNoVat).CalculateAsync(
            LocationWith(false), WeekdayAfternoon, 1m, 0m, CancellationToken.None);

        Assert.Equal(1.33m, exempt.Actual!.RatePerKwh);
        Assert.Null(notExempt.Actual);
    }

    // ---- the cap -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task AHolidayHomeWithinTheCap_IsPricedAtNorgesPris()
    {
        CostResult result = await Calculator().CalculateAsync(
            LocationWith(true, holidayHome: true), WeekdayAfternoon, 2m, 0m, CancellationToken.None);

        Assert.Equal(NorgesPrisTotal, result.Actual!.RatePerKwh);
        Assert.Equal(2m * NorgesPrisTotal, result.Actual.Cost);
        Assert.Null(result.Actual.Cap);
    }

    [Fact]
    public async Task TheHourThatCrossesTheCap_IsSplitByKwh()
    {
        // 999 kWh before: 1 kWh within, 2 above. 1 x 0.976625 + 2 x 1.751625 = 4.479875
        CostResult result = await Calculator().CalculateAsync(
            LocationWith(true, holidayHome: true), WeekdayAfternoon, 3m, 999m, CancellationToken.None);

        RateCost actual = result.Actual!;
        Assert.Equal(4.479875m, actual.Cost);
        Assert.Equal(4.479875m / 3m, actual.RatePerKwh);
        Assert.Equal(NorgesPrisTotal, actual.Breakdown.Total);
        Assert.Equal(new CapSplit(1m, 2m, actual.Cap!.AboveCapBreakdown), actual.Cap);
        Assert.Equal(SpotTotal, actual.Cap.AboveCapBreakdown.Total);
        Assert.Equal(0.02m, actual.Cap.AboveCapBreakdown.Markup);
    }

    [Fact]
    public async Task AnHourAfterTheCap_IsOnSpotTermsWithTheMarkup()
    {
        CostResult result = await Calculator().CalculateAsync(
            LocationWith(true, holidayHome: true), WeekdayAfternoon, 2m, 1000m, CancellationToken.None);

        Assert.Equal(SpotTotal, result.Actual!.RatePerKwh);
        Assert.Equal(2m * SpotTotal, result.Actual.Cost);
        Assert.Equal(0.02m, result.Actual.Breakdown.Markup);
        Assert.Equal(new CapSplit(0m, 2m, result.Actual.Breakdown), result.Actual.Cap);
    }

    [Fact]
    public async Task APermanentHome_UsesTheHigherCap()
    {
        CostResult result = await Calculator().CalculateAsync(
            LocationWith(true), WeekdayAfternoon, 2m, 1000m, CancellationToken.None);

        Assert.Equal(NorgesPrisTotal, result.Actual!.RatePerKwh);
        Assert.Null(result.Actual.Cap);
    }

    [Fact]
    public async Task AnHourWithNoConsumptionAfterTheCap_GetsTheRateOfTheNextKwh()
    {
        CostResult result = await Calculator().CalculateAsync(
            LocationWith(true, holidayHome: true), WeekdayAfternoon, 0m, 1000m, CancellationToken.None);

        Assert.Equal(SpotTotal, result.Actual!.RatePerKwh);
        Assert.Equal(0m, result.Actual.Cost);
    }

    [Fact]
    public async Task ASpotLocation_ComparesWithCappedNorgesPrisTerms()
    {
        CostResult result = await Calculator().CalculateAsync(
            LocationWith(false, holidayHome: true), WeekdayAfternoon, 2m, 1000m, CancellationToken.None);

        Assert.Equal(PricingModel.Spot, result.PricingModel);
        Assert.Equal(result.Actual!.Cost, result.Comparison!.Cost);
        Assert.NotNull(result.Comparison.Cap);
    }

    [Fact]
    public async Task ACapChangeInTheMonth_AppliesFromTheNextMonth()
    {
        // A lower cap from 2026-12-15: December keeps 1,000 kWh (the cap on the 1st), January gets 500.
        IPriceRateRepository rates = Rates([PriceRateStubs.Rate(PriceRateKind.NorgesPrisCapHolidayHome, "2026-12-15", 500m)]);
        Location cabin = LocationWith(true, holidayHome: true);

        CostResult december = await Calculator(rates).CalculateAsync(
            cabin, Utc("2026-12-21T13:00:00Z"), 1m, 600m, CancellationToken.None);
        CostResult january = await Calculator(rates).CalculateAsync(
            cabin, Utc("2027-01-06T13:00:00Z"), 1m, 600m, CancellationToken.None);

        Assert.Null(december.Actual!.Cap);
        Assert.NotNull(january.Actual!.Cap);
    }

    // ---- the month context from the callers ----------------------------------------------------------------------

    private static HourConsumption Hour(Guid locationId, string utc, decimal kwh) =>
        new() { LocationId = locationId, MeterId = Guid.Empty, BucketStart = Utc(utc), AvgPowerWatts = (double)(kwh * 1000m) };

    private static IConsumptionRepository Consumption(params HourConsumption[] rows)
    {
        IConsumptionRepository repository = Substitute.For<IConsumptionRepository>();
        repository.GetHourlyAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<HourConsumption>>(rows
                .Where(r => r.BucketStart >= call.ArgAt<DateTime>(2) && r.BucketStart < call.ArgAt<DateTime>(3))
                .ToList()));
        repository.GetConsumptionKwhAsync(
                Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(rows
                .Where(r => r.BucketStart >= call.ArgAt<DateTime>(1) && r.BucketStart < call.ArgAt<DateTime>(2))
                .Sum(r => r.ConsumptionKwh)));
        return repository;
    }

    [Fact]
    public async Task ARangeStartingMidMonth_CountsTheKwhFromTheStartOfTheMonth()
    {
        Location cabin = LocationWith(true, holidayHome: true);
        IConsumptionRepository consumption = Consumption(
            Hour(cabin.Id, "2026-12-01T10:00:00Z", 999m),
            Hour(cabin.Id, "2026-12-02T13:00:00Z", 3m));

        IReadOnlyList<HourCost> fromMidMonth = await new HourlyCostProvider(consumption, Calculator())
            .GetAsync(cabin, Utc("2026-12-02T00:00:00Z"), Utc("2026-12-03T00:00:00Z"), CancellationToken.None);
        IReadOnlyList<HourCost> wholeMonth = await new HourlyCostProvider(consumption, Calculator())
            .GetAsync(cabin, Utc("2026-11-30T23:00:00Z"), Utc("2026-12-03T00:00:00Z"), CancellationToken.None);

        HourCost hour = Assert.Single(fromMidMonth);
        Assert.Equal(4.479875m, hour.Cost.Actual!.Cost);
        Assert.Equal(hour.Cost.Actual.Cost, wholeMonth.Single(h => h.HourStart == hour.HourStart).Cost.Actual!.Cost);
    }

    [Fact]
    public async Task TheMonthCount_StartsAgainInANewOsloMonth()
    {
        // 2026-11-30T23:00Z is 2026-12-01 00:00 in Oslo: the first hour of December, after 2,000 kWh in November.
        Location cabin = LocationWith(true, holidayHome: true);
        IConsumptionRepository consumption = Consumption(
            Hour(cabin.Id, "2026-11-30T22:00:00Z", 2000m),
            Hour(cabin.Id, "2026-11-30T23:00:00Z", 1m));

        IReadOnlyList<HourCost> hours = await new HourlyCostProvider(consumption, Calculator())
            .GetAsync(cabin, Utc("2026-11-30T22:00:00Z"), Utc("2026-12-01T00:00:00Z"), CancellationToken.None);

        Assert.NotNull(hours[0].Cost.Actual!.Cap);
        Assert.Null(hours[1].Cost.Actual!.Cap);
    }

    [Fact]
    public async Task TheSaving_AddsNothingAboveTheCap()
    {
        Location cabin = LocationWith(true, holidayHome: true);
        IConsumptionRepository consumption = Consumption(
            Hour(cabin.Id, "2026-12-01T10:00:00Z", 1000m),
            Hour(cabin.Id, "2026-12-02T13:00:00Z", 2m));

        IReadOnlyList<HourCost> hours = await new HourlyCostProvider(consumption, Calculator())
            .GetAsync(cabin, Utc("2026-12-02T00:00:00Z"), Utc("2026-12-03T00:00:00Z"), CancellationToken.None);
        SavingPeriodResponse today = SavingCalculator.Calculate(hours, new DateTime(2026, 12, 2)).Periods
            .Single(p => p.Period == SavingCalculator.Today);

        Assert.Equal(0m, today.Saving);
        Assert.Equal(1, today.HoursCounted);
    }

    [Fact]
    public async Task TheDayPrices_SwitchToTheSpotRateOnceTheCapIsPassed()
    {
        // 999 kWh before the day, then 1 kWh in the first hour: from the second hour the next kWh is above the cap.
        Location cabin = LocationWith(true, holidayHome: true);
        IConsumptionRepository consumption = Consumption(
            Hour(cabin.Id, "2026-12-01T10:00:00Z", 999m),
            Hour(cabin.Id, "2026-12-01T23:00:00Z", 1m));
        ILocationRepository<Location> locations = Substitute.For<ILocationRepository<Location>>();
        locations.IsUserAssociatedAsync(Arg.Any<Guid>(), cabin.Id, Arg.Any<CancellationToken>()).Returns(true);
        locations.GetByIdAsync(cabin.Id, Arg.Any<CancellationToken>()).Returns(cabin);
        var handler = new GetDayPricesQueryHandler(
            locations, consumption, Calculator(), Substitute.For<ILogger<GetDayPricesQueryHandler>>());

        Result<DayPricesResponse> result = await handler.Handle(
            new GetDayPricesQuery(Guid.NewGuid(), cabin.Id, new DateTime(2026, 12, 2)), CancellationToken.None);

        // 00:00 Oslo is a night hour: Norgespris (0.40 + 0.20 + 0.0813) x 1.25, spot (1.00 + 0.02 + 0.20 + 0.0813) x 1.25.
        Assert.Equal(0.851625m, result.Value.Items[0].RatePerKwh);
        Assert.Equal(1.626625m, result.Value.Items[1].RatePerKwh);
    }

    // ---- the Elvia bill, October 2026 ---------------------------------------------------------------------------

    [Fact]
    public async Task TheElviaBillOfOctober2026_IsReproducedLineByLine()
    {
        // Elvia from 2026-07-01, excluding taxes and VAT: day 0.2899, night and weekend 0.1699. A holiday home on
        // Norgespris that used 357.62 kWh on weekday days and 641.72 kWh at night and weekends: under its 1,000 kWh cap.
        IPriceRateRepository rates = PriceRateStubs.Repository(
            PriceRateStubs.National2026().Concat(Caps()),
            [
                PriceRateStubs.Grid(Tariff, "2026-01-01", 0.2099m, 0.1299m),
                PriceRateStubs.Grid(Tariff, "2026-07-01", 0.2899m, 0.1699m)
            ]);
        Location cabin = LocationWith(true, holidayHome: true);
        CostCalculator calculator = Calculator(rates);

        // Wednesday 2026-10-14 14:00 Oslo (summer time) and Saturday 2026-10-17 14:00 Oslo.
        CostResult day = await calculator.CalculateAsync(
            cabin, Utc("2026-10-14T12:00:00Z"), 357.62m, 0m, CancellationToken.None);
        CostResult night = await calculator.CalculateAsync(
            cabin, Utc("2026-10-17T12:00:00Z"), 641.72m, 357.62m, CancellationToken.None);

        Assert.Null(day.Actual!.Cap);
        Assert.Null(night.Actual!.Cap);
        AssertLine(129.60m, 357.62m, day.Actual.Breakdown.Grid!.Value);   // Energiledd hverdag 06-22
        AssertLine(136.29m, 641.72m, night.Actual.Breakdown.Grid!.Value); // Energiledd natt og helg
        AssertLine(31.88m, 357.62m, 0.0713m);                             // Elavgift, day kWh
        AssertLine(57.19m, 641.72m, 0.0713m);                             // Elavgift, night kWh
        AssertLine(4.47m, 357.62m, day.Actual.Breakdown.Taxes - 0.0713m); // Energifondet, day kWh
        AssertLine(8.03m, 641.72m, night.Actual.Breakdown.Taxes - 0.0713m);
    }

    /// <summary>A bill line is kWh x the rate excluding VAT x 1.25, rounded by the bill; 0.01 kr per line is allowed.</summary>
    private static void AssertLine(decimal billAmount, decimal kwh, decimal rateExclVat) =>
        Assert.InRange(kwh * rateExclVat * 1.25m, billAmount - 0.01m, billAmount + 0.01m);
}
