using System.Globalization;
using System.Net;
using Application.Common.Interfaces.Repositories;
using Application.ElectricityCost;
using Domain.Common;
using Domain.Common.Entities;
using Features.ElectricityCost.Handlers;
using Features.ElectricityCost.Queries;
using Features.ElectricityCost.Services;
using Features.Tests.Http;
using Features.Tests.NorgesPris;
using Features.Tests.Pricing;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.ElectricityCost;

public class NorgesPrisSavingTests
{
    private static DateTime Utc(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private static RateCost Cost(decimal total, decimal kwh = 1m) =>
        new(total, kwh * total, new PriceBreakdown(total, 0m, null, 0m, 0m, total, false, 0m, 0m, 0m));

    /// <summary>One consumed hour on a Norgespris location: the actual is Norgespris terms, the comparison is spot.</summary>
    private static HourCost NorgesPrisHour(string utc, decimal? norgesPris, decimal? spot, decimal kwh = 1m) =>
        new(Utc(utc), kwh, new CostResult(
            PricingModel.NorgesPris,
            norgesPris is { } n ? Cost(n, kwh) : null,
            spot is { } s ? Cost(s, kwh) : null));

    private static HourCost SpotHour(string utc, decimal? spot, decimal? norgesPris, decimal kwh = 1m) =>
        new(Utc(utc), kwh, new CostResult(
            PricingModel.Spot,
            spot is { } s ? Cost(s, kwh) : null,
            norgesPris is { } n ? Cost(n, kwh) : null));

    private static readonly DateTime Today = new(2026, 11, 16);

    private static SavingPeriodResponse Period(NorgesPrisSavingResponse response, string name) =>
        response.Periods.Single(p => p.Period == name);

    // ---- the sum ----------------------------------------------------------------------------------------------

    [Fact]
    public void TheSaving_IsTheSumOverTheHoursOfSpotCostMinusNorgesPrisCost_AndCanBeNegative()
    {
        HourCost[] hours =
        [
            NorgesPrisHour("2026-11-16T09:00:00Z", norgesPris: 0.98m, spot: 1.75m), // saved 0.77
            NorgesPrisHour("2026-11-16T10:00:00Z", norgesPris: 0.98m, spot: 0.60m), // cost 0.38 more
            NorgesPrisHour("2026-11-16T11:00:00Z", norgesPris: 0.98m, spot: 1.50m, kwh: 2m) // saved 2 x 0.52
        ];

        SavingPeriodResponse today = Period(SavingCalculator.Calculate(hours, Today), "Today");

        Assert.Equal(0.77m - 0.38m + 1.04m, today.Saving);
        Assert.Equal(3, today.HoursCounted);
        Assert.Equal(3, today.HoursWithConsumption);
    }

    [Fact]
    public void ACheapSpotDay_GivesANegativeSaving()
    {
        HourCost[] hours = [NorgesPrisHour("2026-11-16T09:00:00Z", norgesPris: 0.98m, spot: 0.40m)];

        Assert.Equal(-0.58m, Period(SavingCalculator.Calculate(hours, Today), "Today").Saving);
    }

    [Fact]
    public void ASpotLocation_GivesTheSameFigureAsTheNorgesPrisLocationWouldHave()
    {
        HourCost[] norgesPrisLocation =
        [
            NorgesPrisHour("2026-11-16T09:00:00Z", norgesPris: 0.98m, spot: 1.75m),
            NorgesPrisHour("2026-11-16T10:00:00Z", norgesPris: 0.98m, spot: 0.60m)
        ];
        HourCost[] spotLocation =
        [
            SpotHour("2026-11-16T09:00:00Z", spot: 1.75m, norgesPris: 0.98m),
            SpotHour("2026-11-16T10:00:00Z", spot: 0.60m, norgesPris: 0.98m)
        ];

        Assert.Equal(
            Period(SavingCalculator.Calculate(norgesPrisLocation, Today), "Today").Saving,
            Period(SavingCalculator.Calculate(spotLocation, Today), "Today").Saving);
    }

    [Fact]
    public void AnHourWithoutASpotPrice_IsLeftOut_AndCountedSeparately()
    {
        HourCost[] hours =
        [
            NorgesPrisHour("2026-11-16T09:00:00Z", norgesPris: 0.98m, spot: 1.75m),
            NorgesPrisHour("2026-11-16T10:00:00Z", norgesPris: 0.98m, spot: null),
            SpotHour("2026-11-16T11:00:00Z", spot: null, norgesPris: 0.98m)
        ];

        SavingPeriodResponse today = Period(SavingCalculator.Calculate(hours, Today), "Today");

        Assert.Equal(0.77m, today.Saving);
        Assert.Equal(1, today.HoursCounted);
        Assert.Equal(3, today.HoursWithConsumption);
    }

    [Fact]
    public void AnHourWhereNeitherSideCanBePriced_IsLeftOutToo()
    {
        HourCost[] hours = [NorgesPrisHour("2026-11-16T09:00:00Z", norgesPris: null, spot: null)];

        SavingPeriodResponse today = Period(SavingCalculator.Calculate(hours, Today), "Today");

        Assert.Equal(0m, today.Saving);
        Assert.Equal(0, today.HoursCounted);
        Assert.Equal(1, today.HoursWithConsumption);
    }

    // ---- the periods ------------------------------------------------------------------------------------------

    private static HourCost OneKroneSaved(string utc) => NorgesPrisHour(utc, norgesPris: 1.00m, spot: 2.00m);

    [Fact]
    public void ThePeriodsAreOsloCalendarDaysEndingToday_AndTheShorterOnesAddUpToTheLongerOnes()
    {
        HourCost[] hours =
        [
            OneKroneSaved("2026-11-16T09:00:00Z"), // today
            OneKroneSaved("2026-11-12T09:00:00Z"), // in the last 7 days
            OneKroneSaved("2026-11-05T09:00:00Z"), // this month only
            OneKroneSaved("2026-10-20T09:00:00Z"), // this year only
            OneKroneSaved("2025-12-31T09:00:00Z") // last year: in no period
        ];

        NorgesPrisSavingResponse response = SavingCalculator.Calculate(hours, Today);

        Assert.Equal(1m, Period(response, "Today").Saving);
        Assert.Equal(2m, Period(response, "Last7Days").Saving);
        Assert.Equal(3m, Period(response, "MonthToDate").Saving);
        Assert.Equal(4m, Period(response, "YearToDate").Saving);
        Assert.Equal(4, response.Periods.Count);
    }

    [Fact]
    public void ThePeriodsGiveTheirDates()
    {
        NorgesPrisSavingResponse response = SavingCalculator.Calculate([], Today);

        Assert.Equal(new DateOnly(2026, 11, 16), Period(response, "Today").From);
        Assert.Equal(new DateOnly(2026, 11, 16), Period(response, "Today").To);
        Assert.Equal(new DateOnly(2026, 11, 10), Period(response, "Last7Days").From);
        Assert.Equal(new DateOnly(2026, 11, 1), Period(response, "MonthToDate").From);
        Assert.Equal(new DateOnly(2026, 1, 1), Period(response, "YearToDate").From);
        Assert.All(response.Periods, p => Assert.Equal(new DateOnly(2026, 11, 16), p.To));
    }

    [Fact]
    public void TheLastSevenDaysMayStartInThePreviousYear_ButTheMonthAndYearDoNot()
    {
        DateTime early = new(2026, 1, 3);
        HourCost[] hours =
        [
            OneKroneSaved("2026-01-03T09:00:00Z"), // today
            OneKroneSaved("2025-12-30T09:00:00Z") // last 7 days only
        ];

        NorgesPrisSavingResponse response = SavingCalculator.Calculate(hours, early);

        Assert.Equal(2m, Period(response, "Last7Days").Saving);
        Assert.Equal(1m, Period(response, "MonthToDate").Saving);
        Assert.Equal(1m, Period(response, "YearToDate").Saving);
        Assert.Equal(new DateTime(2025, 12, 28), SavingCalculator.EarliestStart(early));
    }

    [Fact]
    public void TheEarliestStart_IsTheStartOfTheYearWhenThatComesFirst() =>
        Assert.Equal(new DateTime(2026, 1, 1), SavingCalculator.EarliestStart(Today));

    [Fact]
    public void AnHourIsPlacedOnItsOsloDay_NotItsUtcDay()
    {
        // 23:00Z on 15 November is 00:00 on 16 November in Oslo: today.
        HourCost[] hours = [OneKroneSaved("2026-11-15T23:00:00Z")];

        Assert.Equal(1m, Period(SavingCalculator.Calculate(hours, Today), "Today").Saving);
    }

    // ---- the handler ------------------------------------------------------------------------------------------

    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IConsumptionRepository _consumption = Substitute.For<IConsumptionRepository>();
    private readonly IElectricityPriceRepository<ElectricityPrice> _prices =
        Substitute.For<IElectricityPriceRepository<ElectricityPrice>>();
    private readonly IExchangeRateRepository<ExchangeRate> _rates = Substitute.For<IExchangeRateRepository<ExchangeRate>>();

    private GetNorgesPrisSavingQueryHandler Handler()
    {
        var calculator = new CostCalculator(_prices, _rates, NorgesPrisRates.Seeded(), PriceRateStubs.Zero());
        return new GetNorgesPrisSavingQueryHandler(
            _locations,
            new HourlyCostProvider(
                _consumption, calculator, Substitute.For<IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample>>(), TimeProvider.System),
            new FixedTimeProvider("2026-11-16T12:00:00Z"),
            Substitute.For<ILogger<GetNorgesPrisSavingQueryHandler>>());
    }

    [Fact]
    public async Task TheHandler_PricesBothSidesWithTheOneCalculation()
    {
        // Spot is 100 EUR/MWh at 10 NOK per EUR = 1.00 NOK/kWh; Norgespris is 0.40; no VAT or taxes in this stub.
        var location = new Location(Guid.NewGuid(), "Home", "Addr", "SN1", "NO1", true, true);
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), location.Id, Arg.Any<CancellationToken>()).Returns(true);
        _locations.GetByIdAsync(location.Id, Arg.Any<CancellationToken>()).Returns(location);
        _prices.GetByRegionAndHourAsync("NO1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => new ElectricityPrice(Guid.NewGuid(), "NO1", call.ArgAt<DateTime>(1), 100m));
        _rates.GetByDateAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => new ExchangeRate(Guid.NewGuid(), "EURNOK", call.ArgAt<DateOnly>(1), 10m));
        _prices.ServeRangesFromSingleHours();
        _rates.ServeRangesFromSingleDates();
        _consumption.GetHourlyAsync(location.Id, null, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<HourConsumption>
            {
                new() { LocationId = location.Id, BucketStart = Utc("2026-11-16T09:00:00Z"), AvgPowerWatts = 2000 },
                new() { LocationId = location.Id, BucketStart = Utc("2026-11-10T09:00:00Z"), AvgPowerWatts = 1000 },
                new() { LocationId = location.Id, BucketStart = Utc("2026-03-10T09:00:00Z"), AvgPowerWatts = 1000 }
            });

        Result<NorgesPrisSavingResponse> result = await Handler()
            .Handle(new GetNorgesPrisSavingQuery(Guid.NewGuid(), location.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1.2m, Period(result.Value, "Today").Saving); // 2 kWh x (1.00 - 0.40)
        Assert.Equal(1.8m, Period(result.Value, "Last7Days").Saving); // plus 1 kWh x 0.60 on 10 November
        Assert.Equal(1.8m, Period(result.Value, "MonthToDate").Saving);
        Assert.Equal(2.4m, Period(result.Value, "YearToDate").Saving); // plus March
        Assert.Equal(3, Period(result.Value, "YearToDate").HoursCounted);
    }

    [Fact]
    public async Task TheMarkup_IsOnBothSides_SoItDoesNotChangeTheSaving()
    {
        // The supplier charges the markup under Norgespris too, so spot terms minus Norgespris terms is still
        // 2 kWh x (1.00 - 0.40) = 1.2 with a markup of 0.05 (no VAT or taxes in this stub).
        var location = new Location(Guid.NewGuid(), "Home", "Addr", "SN1", "NO1", true, true);
        location.SetPricing(null, 0.05m, false, false, false);
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), location.Id, Arg.Any<CancellationToken>()).Returns(true);
        _locations.GetByIdAsync(location.Id, Arg.Any<CancellationToken>()).Returns(location);
        _prices.GetByRegionAndHourAsync("NO1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => new ElectricityPrice(Guid.NewGuid(), "NO1", call.ArgAt<DateTime>(1), 100m));
        _rates.GetByDateAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => new ExchangeRate(Guid.NewGuid(), "EURNOK", call.ArgAt<DateOnly>(1), 10m));
        _prices.ServeRangesFromSingleHours();
        _rates.ServeRangesFromSingleDates();
        _consumption.GetHourlyAsync(location.Id, null, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<HourConsumption>
            {
                new() { LocationId = location.Id, BucketStart = Utc("2026-11-16T09:00:00Z"), AvgPowerWatts = 2000 }
            });

        Result<NorgesPrisSavingResponse> result = await Handler()
            .Handle(new GetNorgesPrisSavingQuery(Guid.NewGuid(), location.Id), CancellationToken.None);

        Assert.Equal(1.2m, Period(result.Value, "Today").Saving);
    }

    [Fact]
    public async Task TheHandler_AUserNotLinkedToTheLocation_GetsNotFound()
    {
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);

        Result<NorgesPrisSavingResponse> result = await Handler()
            .Handle(new GetNorgesPrisSavingQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task TheHandler_AnUnknownLocation_GetsNotFound()
    {
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);

        Result<NorgesPrisSavingResponse> result = await Handler()
            .Handle(new GetNorgesPrisSavingQuery(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ---- the endpoint -----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(HostCaller.Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(HostCaller.User, HttpStatusCode.OK)]
    [InlineData(HostCaller.Admin, HttpStatusCode.OK)]
    public async Task TheEndpoint_NeedsASignIn_ButNotTheAdminRole(HostCaller caller, HttpStatusCode expected)
    {
        await using EndpointHost host = await EndpointHost.StartAsync(
            dispatcher => dispatcher.Send(Arg.Any<GetNorgesPrisSavingQuery>(), Arg.Any<CancellationToken>())
                .Returns(Result.Success(SavingCalculator.Calculate([], Today))),
            caller);

        HttpResponseMessage response = await host.Client.GetAsync($"/api/electricity-cost/saving?locationId={Guid.NewGuid()}");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task TheEndpoint_ReturnsTheFourPeriodsAsJson()
    {
        await using EndpointHost host = await EndpointHost.StartAsync(
            dispatcher => dispatcher.Send(Arg.Any<GetNorgesPrisSavingQuery>(), Arg.Any<CancellationToken>())
                .Returns(Result.Success(SavingCalculator.Calculate([OneKroneSaved("2026-11-16T09:00:00Z")], Today))));

        string json = await host.Client.GetStringAsync($"/api/electricity-cost/saving?locationId={Guid.NewGuid()}");

        foreach (string name in new[] { "Today", "Last7Days", "MonthToDate", "YearToDate" })
        {
            Assert.Contains($"\"period\":\"{name}\"", json);
        }

        Assert.Contains("\"saving\"", json);
        Assert.Contains("\"hoursCounted\"", json);
        Assert.Contains("\"hoursWithConsumption\"", json);
    }

    [Fact]
    public async Task TheEndpoint_ANotFoundFromTheHandler_Answers404()
    {
        await using EndpointHost host = await EndpointHost.StartAsync(
            dispatcher => dispatcher.Send(Arg.Any<GetNorgesPrisSavingQuery>(), Arg.Any<CancellationToken>())
                .Returns(Result.Failure<NorgesPrisSavingResponse>(Error.NotFound("Location.NotFound", "Location not found"))));

        HttpResponseMessage response = await host.Client.GetAsync($"/api/electricity-cost/saving?locationId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
