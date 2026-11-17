using Domain.Common;
using Domain.Common.Entities;
using Features.Pricing.Commands;
using Features.Pricing.Handlers;
using Features.Pricing.Queries;
using Features.Tests.NorgesPris;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Pricing;

public class GridTariffHandlerTests
{
    private static readonly Guid Admin = Guid.NewGuid();
    private const string Now = "2026-11-16T12:00:00Z"; // 13:00 in Oslo on the same day

    private static GridTariff Tariff(string name) => new(Guid.NewGuid(), name);

    private static GridTariffRate Rate(Guid tariffId, string validFrom, decimal day = 0.30m, decimal night = 0.20m) =>
        new(Guid.NewGuid(), tariffId, DateOnly.Parse(validFrom), day, night, Guid.Empty);

    private static CreateGridTariffCommandHandler CreateHandler(FakePriceRateRepository store) =>
        new(store, Substitute.For<ILogger<CreateGridTariffCommandHandler>>());

    private static AddGridTariffRateCommandHandler AddHandler(FakePriceRateRepository store) =>
        new(store, new FixedTimeProvider(Now), Substitute.For<ILogger<AddGridTariffRateCommandHandler>>());

    private static DeleteGridTariffRateCommandHandler DeleteHandler(FakePriceRateRepository store) =>
        new(store, new FixedTimeProvider(Now), Substitute.For<ILogger<DeleteGridTariffRateCommandHandler>>());

    private static AddGridTariffRateCommand Add(Guid tariffId, string validFrom, bool confirm = false) =>
        new(tariffId, DateOnly.Parse(validFrom), 0.35m, 0.22m, confirm, Admin);

    // ---- create -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_AddsAnEmptyTariff()
    {
        var store = new FakePriceRateRepository();

        Result<GridTariffResponse> result = await CreateHandler(store)
            .Handle(new CreateGridTariffCommand("  Elvia household  ", Admin), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Elvia household", result.Value.Name);
        Assert.Empty(result.Value.Rates);
        Assert.Single(store.Tariffs);
    }

    [Fact]
    public async Task Create_ATakenName_IsRefusedWhateverTheCase()
    {
        var store = new FakePriceRateRepository();
        store.Tariffs.Add(Tariff("Elvia household"));

        Result<GridTariffResponse> result = await CreateHandler(store)
            .Handle(new CreateGridTariffCommand("ELVIA HOUSEHOLD", Admin), CancellationToken.None);

        Assert.Equal("Pricing.TariffNameExists", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Single(store.Tariffs);
    }

    // ---- add a rate -------------------------------------------------------------------------------------------

    [Fact]
    public async Task AddRate_ToAnUnknownTariff_IsTariffNotFound()
    {
        Result<GridTariffRateResponse> result = await AddHandler(new FakePriceRateRepository())
            .Handle(Add(Guid.NewGuid(), "2027-01-01"), CancellationToken.None);

        Assert.Equal("Pricing.TariffNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task AddRate_AFutureRate_IsSavedAndUpcoming()
    {
        var store = new FakePriceRateRepository();
        GridTariff tariff = Tariff("A");
        store.Tariffs.Add(tariff);
        store.TariffRates.Add(Rate(tariff.Id, "2026-01-01"));

        Result<GridTariffRateResponse> result =
            await AddHandler(store).Handle(Add(tariff.Id, "2027-01-01"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Upcoming", result.Value.Status);
        Assert.Equal(0.35m, result.Value.DayRatePerKwh);
        Assert.Equal(0.22m, result.Value.NightRatePerKwh);
        Assert.Equal(2, store.TariffRates.Count);
    }

    [Fact]
    public async Task AddRate_TheSameDateOnTwoTariffs_IsAccepted()
    {
        var store = new FakePriceRateRepository();
        GridTariff a = Tariff("A");
        GridTariff b = Tariff("B");
        store.Tariffs.AddRange([a, b]);
        store.TariffRates.Add(Rate(a.Id, "2027-01-01"));

        Result<GridTariffRateResponse> result =
            await AddHandler(store).Handle(Add(b.Id, "2027-01-01"), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AddRate_ADuplicateDateOnTheSameTariff_IsRefused()
    {
        var store = new FakePriceRateRepository();
        GridTariff a = Tariff("A");
        store.Tariffs.Add(a);
        store.TariffRates.Add(Rate(a.Id, "2027-01-01"));

        Result<GridTariffRateResponse> result =
            await AddHandler(store).Handle(Add(a.Id, "2027-01-01"), CancellationToken.None);

        Assert.Equal("Pricing.DateExists", result.Error.Code);
        Assert.Single(store.TariffRates);
    }

    [Fact]
    public async Task AddRate_APastDateNeedsToBeTheNewestOfItsTariffAndConfirmed()
    {
        var store = new FakePriceRateRepository();
        GridTariff a = Tariff("A");
        GridTariff b = Tariff("B");
        store.Tariffs.AddRange([a, b]);
        store.TariffRates.Add(Rate(a.Id, "2026-06-01"));
        store.TariffRates.Add(Rate(b.Id, "2026-01-01"));

        Result<GridTariffRateResponse> unconfirmed =
            await AddHandler(store).Handle(Add(a.Id, "2026-11-01"), CancellationToken.None);
        Result<GridTariffRateResponse> notNewest =
            await AddHandler(store).Handle(Add(a.Id, "2026-03-01", confirm: true), CancellationToken.None);
        // Newer than every row of B although older than a row of A.
        Result<GridTariffRateResponse> otherTariff =
            await AddHandler(store).Handle(Add(b.Id, "2026-03-01", confirm: true), CancellationToken.None);
        Result<GridTariffRateResponse> confirmed =
            await AddHandler(store).Handle(Add(a.Id, "2026-11-01", confirm: true), CancellationToken.None);

        Assert.Equal("Pricing.ConfirmationRequired", unconfirmed.Error.Code);
        Assert.Equal("Pricing.NotNewest", notNewest.Error.Code);
        Assert.True(otherTariff.IsSuccess);
        Assert.True(confirmed.IsSuccess);
        Assert.Equal("InEffect", confirmed.Value.Status);
    }

    [Fact]
    public async Task AddRate_WhenAConcurrentRequestTookTheDate_IsRefusedAsDateExists()
    {
        var store = new FakePriceRateRepository { LoseTheRace = true };
        GridTariff a = Tariff("A");
        store.Tariffs.Add(a);

        Result<GridTariffRateResponse> result =
            await AddHandler(store).Handle(Add(a.Id, "2027-01-01"), CancellationToken.None);

        Assert.Equal("Pricing.DateExists", result.Error.Code);
    }

    // ---- delete a rate ----------------------------------------------------------------------------------------

    [Fact]
    public async Task DeleteRate_AnUpcomingRate_IsRemoved()
    {
        var store = new FakePriceRateRepository();
        GridTariff a = Tariff("A");
        store.Tariffs.Add(a);
        GridTariffRate upcoming = Rate(a.Id, "2027-01-01");
        store.TariffRates.Add(upcoming);

        Result<PricingDeleted> result = await DeleteHandler(store)
            .Handle(new DeleteGridTariffRateCommand(a.Id, upcoming.Id, Admin), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(store.TariffRates);
    }

    [Theory]
    [InlineData("2026-11-16")]
    [InlineData("2026-01-01")]
    public async Task DeleteRate_ARateThatHasStarted_IsRefused(string validFrom)
    {
        var store = new FakePriceRateRepository();
        GridTariff a = Tariff("A");
        store.Tariffs.Add(a);
        GridTariffRate started = Rate(a.Id, validFrom);
        store.TariffRates.Add(started);

        Result<PricingDeleted> result = await DeleteHandler(store)
            .Handle(new DeleteGridTariffRateCommand(a.Id, started.Id, Admin), CancellationToken.None);

        Assert.Equal("Pricing.AlreadyStarted", result.Error.Code);
        Assert.Single(store.TariffRates);
    }

    [Fact]
    public async Task DeleteRate_UnderTheWrongTariff_IsNotFound()
    {
        var store = new FakePriceRateRepository();
        GridTariff a = Tariff("A");
        GridTariff b = Tariff("B");
        store.Tariffs.AddRange([a, b]);
        GridTariffRate upcoming = Rate(a.Id, "2027-01-01");
        store.TariffRates.Add(upcoming);

        Result<PricingDeleted> result = await DeleteHandler(store)
            .Handle(new DeleteGridTariffRateCommand(b.Id, upcoming.Id, Admin), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Single(store.TariffRates);
    }

    // ---- list -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task List_ShowsEachTariffWithItsRatesAndStatuses()
    {
        var store = new FakePriceRateRepository();
        GridTariff a = Tariff("A");
        GridTariff b = Tariff("B");
        store.Tariffs.AddRange([a, b]);
        store.TariffRates.AddRange([Rate(a.Id, "2026-01-01"), Rate(a.Id, "2027-01-01"), Rate(b.Id, "2026-05-01")]);
        var handler = new ListGridTariffsQueryHandler(
            store, new FixedTimeProvider(Now), Substitute.For<ILogger<ListGridTariffsQueryHandler>>());

        Result<IReadOnlyList<GridTariffResponse>> result =
            await handler.Handle(new ListGridTariffsQuery(Admin), CancellationToken.None);

        GridTariffResponse first = result.Value[0];
        Assert.Equal("A", first.Name);
        Assert.Equal(["InEffect", "Upcoming"], first.Rates.Select(r => r.Status));
        Assert.Equal("InEffect", Assert.Single(result.Value[1].Rates).Status);
    }
}
