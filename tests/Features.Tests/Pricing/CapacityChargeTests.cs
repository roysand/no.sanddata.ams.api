using System.Globalization;
using Application.Common.Interfaces.Repositories;
using Application.ElectricityCost;
using Domain.Common;
using Domain.Common.Entities;
using Features.ElectricityCost.Handlers;
using Features.ElectricityCost.Queries;
using Features.Pricing.Commands;
using Features.Pricing.Endpoints;
using Features.Pricing.Handlers;
using Features.Pricing.Queries;
using Features.Pricing.Validators;
using Features.Tests.NorgesPris;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Pricing;

/// <summary>
/// User story 7: the monthly capacity charge with Elvia's 2026 step tables (tariffblad 1.0), stored excluding VAT.
/// The October 2026 bill shows step 5-10 kW at 420 kr including VAT, 336 kr excluding.
/// </summary>
public class CapacityChargeTests
{
    private static readonly Guid Tariff = Guid.NewGuid();

    private static DateTime Utc(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private static readonly (decimal Lower, decimal? Upper)[] ElviaRanges =
    [
        (0, 2), (2, 5), (5, 10), (10, 15), (15, 20), (20, 25), (25, 50), (50, 75), (75, 100), (100, null)
    ];

    /// <summary>Elvia from 2026-01-01 (125 ... 4,570 kr incl. VAT) and from 2026-07-01 (150 ... 6,800 kr incl. VAT).</summary>
    private static readonly decimal[] January = [100, 152, 240, 328, 416, 504, 940, 1376, 1816, 3656];

    private static readonly decimal[] July = [120, 200, 336, 468, 604, 740, 1408, 2080, 2752, 5440];

    private static IEnumerable<GridCapacityStep> Table(string validFrom, decimal[] amounts) =>
        ElviaRanges.Select((r, i) => new GridCapacityStep(
            Guid.NewGuid(), Tariff, DateOnly.Parse(validFrom), r.Lower, r.Upper, amounts[i], Guid.Empty));

    private static IReadOnlyList<GridCapacityStep> ElviaJuly() => Table("2026-07-01", July).ToList();

    private static (DateTime, decimal) H(string utc, decimal kwh) => (Utc(utc), kwh);

    // ---- the calculation --------------------------------------------------------------------------------------

    [Fact]
    public void TheBillMonth_ThreePeaksAverage5Point4_IsStep5To10_420KrWithVat()
    {
        CapacityChargeResult result = CapacityCharge.Calculate(
        [
            H("2026-10-14T16:00:00Z", 6.2m), H("2026-10-14T17:00:00Z", 3.0m),
            H("2026-10-03T05:00:00Z", 5.1m),
            H("2026-10-27T17:00:00Z", 4.9m),
            H("2026-10-20T10:00:00Z", 2.5m)
        ], ElviaJuly(), 0.25m);

        Assert.True(result.Set);
        Assert.Equal([6.2m, 5.1m, 4.9m], result.Peaks.Select(p => p.Kw));
        Assert.Equal(5.4m, result.AverageKw);
        Assert.Equal((5m, 10m), (result.Step!.LowerKw, result.Step.UpperKw));
        Assert.Equal(336m, result.AmountExclVat);
        Assert.Equal(84m, result.Vat);
        Assert.Equal(420m, result.Amount);
        Assert.Equal(4.6m, result.KwToNextStep);
    }

    [Fact]
    public void AnAverageExactlyOnABorder_IsTheHigherStep()
    {
        CapacityChargeResult result = CapacityCharge.Calculate(
            [H("2026-10-01T10:00:00Z", 5m), H("2026-10-02T10:00:00Z", 5m), H("2026-10-03T10:00:00Z", 5m)],
            ElviaJuly(), 0.25m);

        Assert.Equal(5m, result.Step!.LowerKw);
        Assert.Equal(336m, result.AmountExclVat);
    }

    [Fact]
    public void TwoDaysOfData_AreAveragedOverTwo()
    {
        CapacityChargeResult result = CapacityCharge.Calculate(
            [H("2026-10-01T10:00:00Z", 4.0m), H("2026-10-02T10:00:00Z", 6.0m)], ElviaJuly(), 0.25m);

        Assert.Equal(2, result.Peaks.Count);
        Assert.Equal(5.0m, result.AverageKw);
        Assert.Equal(5m, result.Step!.LowerKw);
    }

    [Fact]
    public void TheTwoHighestHoursOnOneDay_CountOnce()
    {
        // Peaks 8 (day 1), 3 (day 2), 2 (day 3): the 7 on day 1 does not count. 13 / 3 = 4.33 kW, step 2-5.
        CapacityChargeResult result = CapacityCharge.Calculate(
        [
            H("2026-10-01T10:00:00Z", 8m), H("2026-10-01T11:00:00Z", 7m),
            H("2026-10-02T10:00:00Z", 3m), H("2026-10-03T10:00:00Z", 2m)
        ], ElviaJuly(), 0.25m);

        Assert.Equal([8m, 3m, 2m], result.Peaks.Select(p => p.Kw));
        Assert.Equal(2m, result.Step!.LowerKw);
        Assert.Equal(200m, result.AmountExclVat);
    }

    [Fact]
    public void TheDayIsTheOsloDay_NotTheUtcDay()
    {
        // 21:00Z is 23:00 on 14 October in Oslo (summer time); 22:00Z is 00:00 on 15 October: two days, two peaks.
        CapacityChargeResult result = CapacityCharge.Calculate(
            [H("2026-10-14T21:00:00Z", 6m), H("2026-10-14T22:00:00Z", 4m)], ElviaJuly(), 0.25m);

        Assert.Equal([new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 15)], result.Peaks.Select(p => p.Date));
        Assert.Equal(5m, result.AverageKw);
    }

    [Fact]
    public void AboveOneHundredKw_IsTheTopStep_WithNoNextStep()
    {
        CapacityChargeResult result = CapacityCharge.Calculate([H("2026-10-01T10:00:00Z", 120m)], ElviaJuly(), 0.25m);

        Assert.Null(result.Step!.UpperKw);
        Assert.Equal(5440m, result.AmountExclVat);
        Assert.Null(result.KwToNextStep);
    }

    [Fact]
    public void NoVat_IsTheAmountExcludingVat()
    {
        CapacityChargeResult result = CapacityCharge.Calculate([H("2026-10-01T10:00:00Z", 6m)], ElviaJuly(), 0m);

        Assert.Equal(336m, result.Amount);
        Assert.Equal(0m, result.Vat);
    }

    [Fact]
    public void NoTableOrNoConsumption_IsNotSet_NeverZero()
    {
        CapacityChargeResult noTable = CapacityCharge.Calculate([H("2026-10-01T10:00:00Z", 6m)], [], 0.25m);
        CapacityChargeResult noHours = CapacityCharge.Calculate([], ElviaJuly(), 0.25m);

        Assert.False(noTable.Set);
        Assert.Null(noTable.Amount);
        Assert.False(noHours.Set);
        Assert.Null(noHours.Amount);
    }

    [Fact]
    public void TheTableOfAMonth_IsTheOneStartedByItsFirstDay()
    {
        GridCapacityStep[] all = [.. Table("2026-01-01", January), .. Table("2026-07-01", July), .. Table("2026-11-15", July)];

        Assert.Equal(240m, CapacityCharge.TableFor(all, new DateOnly(2026, 6, 1))[2].MonthlyAmount);
        Assert.Equal(336m, CapacityCharge.TableFor(all, new DateOnly(2026, 7, 1))[2].MonthlyAmount);
        Assert.Equal(new DateOnly(2026, 7, 1), CapacityCharge.TableFor(all, new DateOnly(2026, 11, 1))[0].ValidFrom);
        Assert.Equal(new DateOnly(2026, 11, 15), CapacityCharge.TableFor(all, new DateOnly(2026, 12, 1))[0].ValidFrom);
        Assert.Empty(CapacityCharge.TableFor(all, new DateOnly(2025, 12, 1)));
    }

    // ---- the query handler ------------------------------------------------------------------------------------

    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly FakePriceRateRepository _rates = new();

    private static HourConsumption Hour(Guid locationId, string utc, decimal kwh) =>
        new() { LocationId = locationId, MeterId = Guid.Empty, BucketStart = Utc(utc), AvgPowerWatts = (double)(kwh * 1000m) };

    private Location Linked(bool withTariff = true, bool vatExempt = false)
    {
        var location = new Location(Guid.NewGuid(), "Cabin", "Addr", "SN1", "NO1", true, true);
        location.SetPricing(withTariff ? Tariff : null, 0m, false, vatExempt, true);
        _locations.IsUserAssociatedAsync(Arg.Any<Guid>(), location.Id, Arg.Any<CancellationToken>()).Returns(true);
        _locations.GetByIdAsync(location.Id, Arg.Any<CancellationToken>()).Returns(location);
        _rates.PriceRates.AddRange(PriceRateStubs.National2026());
        _rates.CapacitySteps.AddRange([.. Table("2026-01-01", January), .. Table("2026-07-01", July)]);
        return location;
    }

    private GetCapacityChargeQueryHandler Handler(Location location, string now, params HourConsumption[] rows)
    {
        IConsumptionRepository consumption = Substitute.For<IConsumptionRepository>();
        consumption.GetHourlyAsync(
                location.Id, null, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<HourConsumption>>(rows
                .Where(r => r.BucketStart >= call.ArgAt<DateTime>(2) && r.BucketStart < call.ArgAt<DateTime>(3))
                .ToList()));
        return new GetCapacityChargeQueryHandler(
            _locations, consumption, _rates, new FixedTimeProvider(now),
            Substitute.For<ILogger<GetCapacityChargeQueryHandler>>());
    }

    private static HourConsumption[] October(Guid id) =>
    [
        Hour(id, "2026-10-14T16:00:00Z", 6.2m), Hour(id, "2026-10-03T05:00:00Z", 5.1m),
        Hour(id, "2026-10-27T17:00:00Z", 4.9m), Hour(id, "2026-11-02T10:00:00Z", 50m) // November: not counted
    ];

    [Fact]
    public async Task Handler_ForTheBillMonth_Gives420KrWithVat_NotProvisional()
    {
        Location cabin = Linked();

        Result<CapacityChargeResponse> result = await Handler(cabin, "2026-11-17T12:00:00Z", October(cabin.Id))
            .Handle(new GetCapacityChargeQuery(Guid.NewGuid(), cabin.Id, new DateOnly(2026, 10, 1)), CancellationToken.None);

        Assert.True(result.Value.Set);
        Assert.False(result.Value.Provisional);
        Assert.Equal("2026-10", result.Value.Month);
        Assert.Equal(5.4m, result.Value.AverageKw);
        Assert.Equal(420m, result.Value.Amount);
    }

    [Fact]
    public async Task Handler_TheCurrentMonth_IsProvisional()
    {
        Location cabin = Linked();

        Result<CapacityChargeResponse> result = await Handler(cabin, "2026-10-20T12:00:00Z", October(cabin.Id))
            .Handle(new GetCapacityChargeQuery(Guid.NewGuid(), cabin.Id, new DateOnly(2026, 10, 1)), CancellationToken.None);

        Assert.True(result.Value.Provisional);
    }

    [Fact]
    public async Task Handler_NoMonth_IsTheCurrentOsloMonth()
    {
        // 2026-10-31T23:30Z is already 1 November in Oslo (winter time, UTC+1).
        Location cabin = Linked();

        Result<CapacityChargeResponse> result = await Handler(cabin, "2026-10-31T23:30:00Z", October(cabin.Id))
            .Handle(new GetCapacityChargeQuery(Guid.NewGuid(), cabin.Id, null), CancellationToken.None);

        Assert.Equal("2026-11", result.Value.Month);
        Assert.True(result.Value.Provisional);
        Assert.Equal(50m, result.Value.AverageKw);
    }

    [Fact]
    public async Task Handler_AMonthBeforeTheJulyTable_UsesTheJanuaryAmounts()
    {
        Location cabin = Linked();

        Result<CapacityChargeResponse> result = await Handler(cabin, "2026-11-17T12:00:00Z",
                Hour(cabin.Id, "2026-06-10T10:00:00Z", 6m))
            .Handle(new GetCapacityChargeQuery(Guid.NewGuid(), cabin.Id, new DateOnly(2026, 6, 1)), CancellationToken.None);

        Assert.Equal(240m, result.Value.AmountExclVat);
        Assert.Equal(300m, result.Value.Amount);
    }

    [Fact]
    public async Task Handler_AVatExemptLocation_Pays336()
    {
        Location cabin = Linked(vatExempt: true);

        Result<CapacityChargeResponse> result = await Handler(cabin, "2026-11-17T12:00:00Z", October(cabin.Id))
            .Handle(new GetCapacityChargeQuery(Guid.NewGuid(), cabin.Id, new DateOnly(2026, 10, 1)), CancellationToken.None);

        Assert.Equal(336m, result.Value.Amount);
    }

    [Fact]
    public async Task Handler_NoTariff_IsNotSet()
    {
        Location cabin = Linked(withTariff: false);

        Result<CapacityChargeResponse> result = await Handler(cabin, "2026-11-17T12:00:00Z", October(cabin.Id))
            .Handle(new GetCapacityChargeQuery(Guid.NewGuid(), cabin.Id, new DateOnly(2026, 10, 1)), CancellationToken.None);

        Assert.False(result.Value.Set);
        Assert.Null(result.Value.Amount);
        Assert.Equal(3, result.Value.Peaks.Count);
    }

    [Fact]
    public async Task Handler_AUserNotLinked_GetsNotFound()
    {
        Location cabin = Linked();
        Guid stranger = Guid.NewGuid();
        _locations.IsUserAssociatedAsync(stranger, cabin.Id, Arg.Any<CancellationToken>()).Returns(false);

        Result<CapacityChargeResponse> result = await Handler(cabin, "2026-11-17T12:00:00Z")
            .Handle(new GetCapacityChargeQuery(stranger, cabin.Id, new DateOnly(2026, 10, 1)), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ---- the Admin step tables ---------------------------------------------------------------------------------

    private static readonly Guid Admin = Guid.NewGuid();
    private const string Now = "2026-11-17T12:00:00Z";

    private static IReadOnlyList<CapacityStepInput> Inputs(decimal[] amounts) =>
        ElviaRanges.Select((r, i) => new CapacityStepInput(r.Lower, r.Upper, amounts[i])).ToList();

    private GridTariff KnownTariff()
    {
        var tariff = new GridTariff(Tariff, "Elvia");
        _rates.Tariffs.Add(tariff);
        return tariff;
    }

    private AddCapacityStepsCommandHandler AddHandler() =>
        new(_rates, new FixedTimeProvider(Now), Substitute.For<ILogger<AddCapacityStepsCommandHandler>>());

    private DeleteCapacityStepsCommandHandler DeleteHandler() =>
        new(_rates, new FixedTimeProvider(Now), Substitute.For<ILogger<DeleteCapacityStepsCommandHandler>>());

    private ListCapacityStepsQueryHandler ListHandler() =>
        new(_rates, new FixedTimeProvider(Now), Substitute.For<ILogger<ListCapacityStepsQueryHandler>>());

    private Task<Result<CapacityStepTableResponse>> Add(string validFrom, bool confirm = false) =>
        AddHandler().Handle(
            new AddCapacityStepsCommand(Tariff, DateOnly.Parse(validFrom), Inputs(July), confirm, Admin),
            CancellationToken.None);

    [Fact]
    public async Task Steps_AFutureTable_IsAddedAsAWhole_AndListedAsUpcoming()
    {
        KnownTariff();
        _rates.CapacitySteps.AddRange(Table("2026-07-01", July));

        Result<CapacityStepTableResponse> added = await Add("2027-01-01");
        Result<IReadOnlyList<CapacityStepTableResponse>> listed =
            await ListHandler().Handle(new ListCapacityStepsQuery(Tariff, Admin), CancellationToken.None);

        Assert.True(added.IsSuccess);
        Assert.Equal(10, added.Value.Steps.Count);
        Assert.Equal("Upcoming", added.Value.Status);
        Assert.Equal(["InEffect", "Upcoming"], listed.Value.Select(t => t.Status));
    }

    [Fact]
    public async Task Steps_TheDatedRules_ApplyPerTable()
    {
        KnownTariff();
        _rates.CapacitySteps.AddRange(Table("2026-07-01", July));

        Assert.Equal("Pricing.DateExists", (await Add("2026-07-01", confirm: true)).Error.Code);
        Assert.Equal("Pricing.NotNewest", (await Add("2026-06-01", confirm: true)).Error.Code);
        Assert.Equal("Pricing.ConfirmationRequired", (await Add("2026-11-01")).Error.Code);
        Assert.True((await Add("2026-11-01", confirm: true)).IsSuccess);
    }

    [Fact]
    public async Task Steps_AnUnknownTariff_IsNotFound()
    {
        Assert.Equal("Pricing.TariffNotFound", (await Add("2027-01-01")).Error.Code);
    }

    [Fact]
    public async Task Steps_OnlyAnUpcomingTableCanBeDeleted_AllItsStepsTogether()
    {
        KnownTariff();
        _rates.CapacitySteps.AddRange([.. Table("2026-07-01", July), .. Table("2027-01-01", July)]);

        Result<CapacityStepsDeleted> started = await DeleteHandler().Handle(
            new DeleteCapacityStepsCommand(Tariff, new DateOnly(2026, 7, 1), Admin), CancellationToken.None);
        Result<CapacityStepsDeleted> unknown = await DeleteHandler().Handle(
            new DeleteCapacityStepsCommand(Tariff, new DateOnly(2027, 2, 1), Admin), CancellationToken.None);
        Result<CapacityStepsDeleted> upcoming = await DeleteHandler().Handle(
            new DeleteCapacityStepsCommand(Tariff, new DateOnly(2027, 1, 1), Admin), CancellationToken.None);

        Assert.Equal("Pricing.AlreadyStarted", started.Error.Code);
        Assert.Equal("Pricing.NotFound", unknown.Error.Code);
        Assert.True(upcoming.IsSuccess);
        Assert.All(_rates.CapacitySteps, s => Assert.Equal(new DateOnly(2026, 7, 1), s.ValidFrom));
        Assert.Equal(10, _rates.CapacitySteps.Count);
    }

    // ---- the step-table validator ------------------------------------------------------------------------------

    private static bool Valid(params CapacityStepInput[] steps) =>
        new AddCapacityStepsValidator()
            .Validate(new AddCapacityStepsRequest(Tariff, new DateOnly(2027, 1, 1), steps)).IsValid;

    [Fact]
    public void Validator_ElviasTable_Passes() =>
        Assert.True(Valid([.. Inputs(July)]));

    [Fact]
    public void Validator_ASingleOpenStep_Passes() =>
        Assert.True(Valid(new CapacityStepInput(0, null, 100)));

    [Fact]
    public void Validator_BrokenTables_AreRefused()
    {
        Assert.False(Valid());                                                                         // empty
        Assert.False(Valid(new CapacityStepInput(1, 2, 10), new CapacityStepInput(2, null, 20)));      // not at 0
        Assert.False(Valid(new CapacityStepInput(0, 2, 10), new CapacityStepInput(3, null, 20)));      // gap
        Assert.False(Valid(new CapacityStepInput(0, 2, 10), new CapacityStepInput(2, 5, 20)));         // top not open
        Assert.False(Valid(new CapacityStepInput(0, null, 10), new CapacityStepInput(2, null, 20)));   // open not last
        Assert.False(Valid(new CapacityStepInput(0, 0, 10), new CapacityStepInput(0, null, 20)));      // empty range
        Assert.False(Valid(new CapacityStepInput(0, 2, -1), new CapacityStepInput(2, null, 20)));      // negative
        Assert.False(Valid(new CapacityStepInput(0, 2, 10.00001m), new CapacityStepInput(2, null, 20))); // decimals
    }
}
