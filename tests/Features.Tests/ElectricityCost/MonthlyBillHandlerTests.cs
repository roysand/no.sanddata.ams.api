using System.Globalization;
using Application.Common.Interfaces.Repositories;
using Application.ElectricityCost;
using Domain.Common;
using Domain.Common.Entities;
using Features.ElectricityCost.Handlers;
using Features.ElectricityCost.Queries;
using Features.ElectricityCost.Services;
using Features.Tests.NorgesPris;
using Features.Tests.Pricing;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.ElectricityCost;

/// <summary>
/// The bill handler: the month, access, the hours so far, the capacity charge and the counts. The line arithmetic is
/// in <see cref="MonthlyBillBuilderTests"/>. Spot 1.00 NOK per kWh, Norgespris 0.40, grid 0.30 / 0.20, 2026 taxes and
/// VAT 25 percent, one capacity step from 0 kW at 100 excluding VAT.
/// </summary>
public class MonthlyBillHandlerTests
{
    private static readonly Guid Tariff = Guid.NewGuid();

    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IConsumptionRepository _consumption = Substitute.For<IConsumptionRepository>();
    private readonly IElectricityPriceRepository<ElectricityPrice> _prices =
        Substitute.For<IElectricityPriceRepository<ElectricityPrice>>();
    private readonly IExchangeRateRepository<ExchangeRate> _fx = Substitute.For<IExchangeRateRepository<ExchangeRate>>();
    private readonly IPriceRateRepository _rates;
    private readonly ILogger<GetMonthlyBillQueryHandler> _logger = Substitute.For<ILogger<GetMonthlyBillQueryHandler>>();
    private readonly List<HourConsumption> _readings = [];

    /// <summary>The spot price in EUR/MWh of an hour, or null when it has not been published; 100 by default.</summary>
    private Func<DateTime, decimal?> _spot = _ => 100m;
    private readonly Location _cabin;

    public MonthlyBillHandlerTests()
    {
        _rates = PriceRateStubs.Repository(
            PriceRateStubs.National2026().Append(
                PriceRateStubs.Rate(PriceRateKind.NorgesPrisCapHolidayHome, "2025-10-01", 1000m)),
            [PriceRateStubs.Grid(Tariff, "2026-01-01", 0.30m, 0.20m)]);
        _rates.GetCapacityStepsAsync(Tariff, Arg.Any<CancellationToken>())
            .Returns([new GridCapacityStep(Guid.NewGuid(), Tariff, new DateOnly(2026, 1, 1), 0m, null, 100m, Guid.Empty)]);

        _prices.GetByRegionAndHourAsync("NO1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => _spot(call.ArgAt<DateTime>(1)) is { } eur
                ? new ElectricityPrice(Guid.NewGuid(), "NO1", call.ArgAt<DateTime>(1), eur)
                : null);
        _fx.GetByDateAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => new ExchangeRate(Guid.NewGuid(), "EURNOK", call.ArgAt<DateOnly>(1), 10m));
        _prices.ServeRangesFromSingleHours();
        _fx.ServeRangesFromSingleDates();

        _consumption.GetHourlyAsync(
                Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<HourConsumption>>(_readings
                .Where(r => r.BucketStart >= call.ArgAt<DateTime>(2) && r.BucketStart < call.ArgAt<DateTime>(3))
                .ToList()));

        _cabin = new Location(Guid.NewGuid(), "Cabin", "Addr", "SN1", "NO1", true, true);
        _cabin.SetPricing(Tariff, 0.02m, false, false, true);
        _cabin.SetSupplierMonthlyFee(27.20m);
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), _cabin.Id, Arg.Any<CancellationToken>()).Returns(true);
        _locations.GetByIdAsync(_cabin.Id, Arg.Any<CancellationToken>()).Returns(_cabin);
    }

    private static DateTime Utc(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private void Reading(string utc, decimal kwh) => _readings.Add(new HourConsumption
    {
        LocationId = _cabin.Id, MeterId = Guid.Empty, BucketStart = Utc(utc), AvgPowerWatts = (double)(kwh * 1000m)
    });

    private GetMonthlyBillQueryHandler Handler(string now = "2026-12-10T12:30:00Z")
    {
        var calculator = new CostCalculator(_prices, _fx, NorgesPrisRates.Seeded(), _rates);
        return new GetMonthlyBillQueryHandler(
            _locations,
            _consumption,
            _rates,
            new HourlyCostProvider(_consumption, calculator),
            calculator,
            new FixedTimeProvider(now),
            _logger);
    }

    private Task<Result<MonthlyBillResponse>> Bill(string? month, string now = "2026-12-10T12:30:00Z") =>
        Handler(now).Handle(
            new GetMonthlyBillQuery(
                Guid.NewGuid(),
                _cabin.Id,
                month is null ? null : DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture)),
            CancellationToken.None);

    private static BillLineResponse Line(MonthlyBillResponse bill, string code) =>
        bill.Strom.Lines.Concat(bill.Nettleie.Lines).Single(l => l.Code == code);

    [Fact]
    public async Task AMonthThatHasEnded_IsFinal_WithNoProjection()
    {
        Reading("2026-11-04T13:00:00Z", 2m); // Wednesday 14:00 Oslo, a day hour
        Reading("2026-11-08T13:00:00Z", 3m); // Sunday

        Result<MonthlyBillResponse> result = await Bill("2026-11");

        Assert.True(result.IsSuccess);
        MonthlyBillResponse bill = result.Value;
        Assert.Equal("2026-11", bill.Month);
        Assert.Equal("Final", bill.Status);
        Assert.Equal("NorgesPris", bill.PricingModel);
        Assert.Null(bill.Projection);
        Assert.False(bill.Capacity.Provisional);
        Assert.All(bill.Strom.Lines.Concat(bill.Nettleie.Lines), l => Assert.Equal(0m, l.Projected));
        Assert.Equal(0m, bill.Total.Projected);
        Assert.Equal(new BillHoursResponse(720, 720, 2, 2), bill.Hours);
        Assert.Equal(163.633125m, bill.Total.Amount); // the same hours as MonthlyBillBuilderTests, by hand
        Assert.Equal(-1.0m * 0.60m * 1.25m, Line(bill, "NorgesPrisCredit").UnitPrice);
        Assert.Equal(34.00m, Line(bill, "SupplierMonthlyFee").UnitPrice);
        Assert.True(bill.IsComplete);
    }

    [Fact]
    public async Task TheLineOrder_IsTheInvoiceOrder()
    {
        Reading("2026-11-04T13:00:00Z", 1m);

        MonthlyBillResponse bill = (await Bill("2026-11")).Value;

        Assert.Equal(["SupplierMonthlyFee", "SpotEnergy", "SupplierMarkup"], bill.Strom.Lines.Select(l => l.Code));
        Assert.Equal(
            ["GridEnergyDay", "GridEnergyNight", "CapacityCharge", "ElectricityTax", "EnovaFee", "NorgesPrisCredit"],
            bill.Nettleie.Lines.Select(l => l.Code));
        Assert.Equal("Month", Line(bill, "CapacityCharge").Unit);
        Assert.Equal("kWh", Line(bill, "SpotEnergy").Unit);
    }

    [Fact]
    public async Task TheCurrentMonth_CountsTheHoursBeforeTheCurrentHour_AndIsAnEstimate()
    {
        // Now is 2026-12-10 12:30 UTC; December starts 2026-11-30 23:00 UTC, so 229 hours have passed (to 12:00 UTC).
        Reading("2026-12-02T13:00:00Z", 2m);
        Reading("2026-12-10T12:00:00Z", 5m); // the current hour: not part of the hours so far

        MonthlyBillResponse bill = (await Bill(null)).Value;

        Assert.Equal("2026-12", bill.Month);
        Assert.Equal("Estimate", bill.Status);
        Assert.True(bill.Capacity.Provisional);
        Assert.Equal(new BillHoursResponse(744, 229, 1, 1), bill.Hours);
        Assert.Equal(2m, Line(bill, "SpotEnergy").Quantity);
    }

    [Fact]
    public async Task AMonthThatHasNotStarted_IsRefused()
    {
        Result<MonthlyBillResponse> result = await Bill("2027-01");

        Assert.False(result.IsSuccess);
        Assert.Equal("Bill.MonthNotStarted", result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task AUserNotLinkedToTheLocation_GetsNotFound_AndTheRefusalIsLogged()
    {
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), _cabin.Id, Arg.Any<CancellationToken>()).Returns(false);
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        Result<MonthlyBillResponse> result = await Bill("2026-11");

        Assert.Equal("Location.NotFound", result.Error.Code);
        _logger.Received(1).Log(
            LogLevel.Warning, Arg.Is<EventId>(e => e.Id == 1504), Arg.Any<object>(), null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task TheCapacityLine_IsTheCapacityChargeOfTheSameMonth()
    {
        Reading("2026-11-04T13:00:00Z", 6m);
        Reading("2026-11-05T13:00:00Z", 4m);
        var capacityHandler = new GetCapacityChargeQueryHandler(
            _locations, _consumption, _rates, new FixedTimeProvider("2026-12-10T12:30:00Z"),
            Substitute.For<ILogger<GetCapacityChargeQueryHandler>>());

        MonthlyBillResponse bill = (await Bill("2026-11")).Value;
        CapacityChargeResponse charge = (await capacityHandler.Handle(
            new GetCapacityChargeQuery(Guid.NewGuid(), _cabin.Id, new DateOnly(2026, 11, 1)),
            CancellationToken.None)).Value;

        Assert.Equal(charge.Amount, Line(bill, "CapacityCharge").Amount);
        Assert.Equal(charge.AverageKw, bill.Capacity.AverageKw);
        Assert.Equal(charge.Step, bill.Capacity.Step);
    }

    [Fact]
    public async Task TheDatedRates_AreLoadedOnceForTheWholeMonth()
    {
        for (int day = 0; day < 30; day++)
        {
            Reading($"2026-11-{day + 1:00}T13:00:00Z", 1m);
        }

        await Bill("2026-11");

        await _rates.Received(1).GetPriceRatesAsync(Arg.Any<CancellationToken>());
        await _rates.Received(1).GetGridTariffRatesAsync(Arg.Any<CancellationToken>());
        await _prices.Received(1).GetByRegionAndHourRangeAsync(
            Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    // ---- the current month: the projection (user story 3) -----------------------------------------------------

    // Now is Thursday 2026-12-10 12:30 UTC. The current hour starts 12:00 UTC; the window is the 168 hours from
    // 2026-12-03 12:00 UTC; December ends 2026-12-31 23:00 UTC, so 515 hours are projected.
    private static readonly DateTime CurrentHour = Utc("2026-12-10T12:00:00Z");
    private static readonly DateTime WindowStart = Utc("2026-12-03T12:00:00Z");

    private void ReadingsEveryHour(DateTime from, DateTime to, decimal kwh)
    {
        for (DateTime hour = from; hour < to; hour = hour.AddHours(1))
        {
            Reading(hour.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), kwh);
        }
    }

    private Location SpotHome()
    {
        var home = new Location(Guid.NewGuid(), "Home", "Addr", "SN2", "NO1", true, false);
        home.SetPricing(Tariff, 0.02m, false, false, false);
        home.SetSupplierMonthlyFee(27.20m);
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), home.Id, Arg.Any<CancellationToken>()).Returns(true);
        _locations.GetByIdAsync(home.Id, Arg.Any<CancellationToken>()).Returns(home);
        return home;
    }

    [Fact]
    public async Task TheCurrentMonth_ProjectsEveryHourFromTheCurrentHourToTheMonthEnd()
    {
        ReadingsEveryHour(WindowStart, CurrentHour, 1m);

        MonthlyBillResponse bill = (await Bill(null)).Value;

        Assert.Equal("Estimate", bill.Status);
        BillProjectionResponse projection = Assert.IsType<BillProjectionResponse>(bill.Projection);
        Assert.Equal(CurrentHour, projection.FromUtc);
        Assert.Equal(515, projection.Hours);
        Assert.Equal(515m, projection.Kwh); // 1 kWh in every hour of last week
        Assert.Equal(168, projection.BasedOnHours);
        Assert.Equal(7m, projection.BasedOnDays);
        Assert.Equal(1.00m, projection.AssumedSpotPricePerKwh);
        Assert.True(bill.Capacity.Provisional);
        Assert.Equal(168m + 515m, Line(bill, "SpotEnergy").Quantity);
        Assert.Equal(bill.Total.Amount, bill.Total.SoFar + bill.Total.Projected);
        Assert.All(bill.Strom.Lines.Concat(bill.Nettleie.Lines).Where(l => l.Set),
            l => Assert.Equal(l.Amount, l.SoFar + l.Projected));
    }

    [Fact]
    public async Task TheSoFarAmounts_AreTheFinishedMonthCalculationOfTheSameHours()
    {
        ReadingsEveryHour(Utc("2026-12-01T00:00:00Z"), CurrentHour, 0.5m);
        var calculator = new CostCalculator(_prices, _fx, NorgesPrisRates.Seeded(), _rates);
        IReadOnlyList<HourCost> hours = await new HourlyCostProvider(_consumption, calculator)
            .GetAsync(_cabin, Utc("2026-11-30T23:00:00Z"), CurrentHour, CancellationToken.None);
        CapacityChargeResult capacity = CapacityCharge.Calculate(
            hours.Select(h => (h.HourStart, h.ConsumptionKwh)),
            [new GridCapacityStep(Guid.NewGuid(), Tariff, new DateOnly(2026, 1, 1), 0m, null, 100m, Guid.Empty)],
            0.25m);
        MonthlyBill expected = MonthlyBillBuilder.Build(_cabin, hours, [], capacity, 0.25m);

        MonthlyBillResponse bill = (await Bill(null)).Value;

        foreach (BillLine line in expected.Strom.Lines.Concat(expected.Nettleie.Lines))
        {
            Assert.Equal(line.Amount, Line(bill, line.Code.ToString()).SoFar);
        }

        Assert.Equal(expected.Amount, bill.Total.SoFar);
    }

    [Fact]
    public async Task PublishedPricesAreUsed_AndTheWindowAverageForTheOtherHours()
    {
        // Window hours 50 EUR/MWh (0.50 NOK/kWh); published from the current hour to 2026-12-11 23:00 UTC (35 hours)
        // at 80 (0.80); nothing published after that. Projected spot energy: (35 x 0.80 + 480 x 0.50) x 1.25 = 335.
        Location home = SpotHome();
        _readings.Clear();
        for (DateTime hour = WindowStart; hour < CurrentHour; hour = hour.AddHours(1))
        {
            _readings.Add(new HourConsumption
            {
                LocationId = home.Id, MeterId = Guid.Empty, BucketStart = hour, AvgPowerWatts = 1000
            });
        }

        DateTime published = Utc("2026-12-11T23:00:00Z");
        _spot = hour => hour < WindowStart ? 100m : hour < CurrentHour ? 50m : hour < published ? 80m : null;

        MonthlyBillResponse bill = (await Handler().Handle(
            new GetMonthlyBillQuery(Guid.NewGuid(), home.Id, null), CancellationToken.None)).Value;

        Assert.Equal(0.50m, bill.Projection!.AssumedSpotPricePerKwh);
        Assert.Equal(335m, Line(bill, "SpotEnergy").Projected);
        Assert.Equal(168m * 0.50m * 1.25m, Line(bill, "SpotEnergy").SoFar);
        Assert.True(bill.IsComplete);
    }

    [Fact]
    public async Task AProjectionThatPassesTheCap_PricesTheExcessOnSpotTermsWithNoCredit()
    {
        // 3 kWh an hour: 504 kWh so far (window only), 1,545 projected; the holiday-home cap is 1,000 kWh.
        ReadingsEveryHour(WindowStart, CurrentHour, 3m);

        MonthlyBillResponse bill = (await Bill(null)).Value;

        Assert.Equal(1000m, Line(bill, "NorgesPrisCredit").Quantity);
        Assert.Equal(504m + 1545m, Line(bill, "SpotEnergy").Quantity);
        Assert.Equal(-1000m * 0.60m * 1.25m, Line(bill, "NorgesPrisCredit").Amount);
    }

    [Fact]
    public async Task ANorgesPrisTotal_DoesNotDependOnTheAssumedSpotPrice()
    {
        ReadingsEveryHour(WindowStart, CurrentHour, 1m);
        _spot = hour => hour < CurrentHour ? 50m : null;
        MonthlyBillResponse cheap = (await Bill(null)).Value;

        _spot = hour => hour < CurrentHour ? 150m : null;
        MonthlyBillResponse dear = (await Bill(null)).Value;

        Assert.NotEqual(cheap.Projection!.AssumedSpotPricePerKwh, dear.Projection!.AssumedSpotPricePerKwh);
        Assert.NotEqual(cheap.Strom.Amount, dear.Strom.Amount);
        Assert.Equal(cheap.Total.Amount, dear.Total.Amount);
    }

    [Fact]
    public async Task OnTheFirstDayOfTheMonth_TheWindowIsTheLastWeekOfThePreviousMonth()
    {
        // 2026-12-01 06:30 Oslo is 05:30 UTC: the window is 2026-11-24 05:00 to 2026-12-01 05:00 UTC.
        ReadingsEveryHour(Utc("2026-11-24T05:00:00Z"), Utc("2026-12-01T05:00:00Z"), 2m);

        MonthlyBillResponse bill = (await Bill(null, now: "2026-12-01T05:30:00Z")).Value;

        Assert.Equal(168, bill.Projection!.BasedOnHours);
        Assert.Equal(2m * bill.Projection.Hours, bill.Projection.Kwh);
        Assert.Equal(6, bill.Hours.Passed); // 23:00 to 05:00 UTC
        Assert.Equal(6m * 2m, Line(bill, "SpotEnergy").Quantity - bill.Projection.Kwh);
    }

    [Fact]
    public async Task ALocationWithoutReadings_ProjectsZeroKwh_BasedOnZeroDays()
    {
        MonthlyBillResponse bill = (await Bill(null)).Value;

        Assert.Equal(0m, bill.Projection!.Kwh);
        Assert.Equal(0, bill.Projection.BasedOnHours);
        Assert.Equal(0m, bill.Projection.BasedOnDays);
        Assert.Equal(515, bill.Projection.Hours);
    }
}
