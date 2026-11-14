using Application.ElectricityCost;
using Domain.Common;
using Domain.Common.Entities;
using Features.NorgesPris.Commands;
using Features.NorgesPris.Handlers;
using Features.NorgesPris.Queries;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.NorgesPris;

public class NorgesPrisHandlerTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    // 2026-11-13 12:00 UTC is 13:00 in Oslo on the same day.
    private static readonly string Now = "2026-11-13T12:00:00Z";

    private readonly NorgesPrisRate _seeded = NorgesPrisRates.Rate("2020-01-01", 0.40m);

    private FakeNorgesPrisRateRepository Store(params NorgesPrisRate[] more)
    {
        var repository = new FakeNorgesPrisRateRepository(_seeded);
        repository.Stored.AddRange(more);
        return repository;
    }

    private static AddNorgesPrisRateCommandHandler AddHandler(FakeNorgesPrisRateRepository store, string now = "2026-11-13T12:00:00Z") =>
        new(store, new FixedTimeProvider(now), Substitute.For<ILogger<AddNorgesPrisRateCommandHandler>>());

    private static DeleteNorgesPrisRateCommandHandler DeleteHandler(FakeNorgesPrisRateRepository store, string now = "2026-11-13T12:00:00Z") =>
        new(store, new FixedTimeProvider(now), Substitute.For<ILogger<DeleteNorgesPrisRateCommandHandler>>());

    private static ListNorgesPrisRatesQueryHandler ListHandler(FakeNorgesPrisRateRepository store, string now) =>
        new(store, new FixedTimeProvider(now), Substitute.For<ILogger<ListNorgesPrisRatesQueryHandler>>());

    private static AddNorgesPrisRateCommand Add(string validFrom, decimal rate = 0.50m, bool confirm = false) =>
        new(DateOnly.Parse(validFrom), rate, 0m, confirm, Admin);

    // ---- list -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task List_MarksExactlyOneRateInEffectAndTheFutureOnesUpcoming()
    {
        FakeNorgesPrisRateRepository store = Store(
            NorgesPrisRates.Rate("2026-01-01", 0.45m), NorgesPrisRates.Rate("2027-01-01", 0.50m));

        Result<IReadOnlyList<NorgesPrisRateResponse>> result =
            await ListHandler(store, Now).Handle(new ListNorgesPrisRatesQuery(Admin), CancellationToken.None);

        Assert.Equal(["Superseded", "InEffect", "Upcoming"], result.Value.Select(r => r.Status));
        Assert.Equal(["2020-01-01", "2026-01-01", "2027-01-01"], result.Value.Select(r => r.ValidFrom.ToString("yyyy-MM-dd")));
    }

    [Fact]
    public async Task List_ARateStartingToday_IsInEffect()
    {
        FakeNorgesPrisRateRepository store = Store(NorgesPrisRates.Rate("2026-11-13", 0.50m, 0.02m));

        Result<IReadOnlyList<NorgesPrisRateResponse>> result =
            await ListHandler(store, Now).Handle(new ListNorgesPrisRatesQuery(Admin), CancellationToken.None);

        Assert.Equal("InEffect", result.Value[1].Status);
        Assert.Equal("Superseded", result.Value[0].Status);
        Assert.Equal(0.52m, result.Value[1].TotalPerKwh);
    }

    [Fact]
    public async Task List_UsesTheOsloDateNotTheUtcDate()
    {
        FakeNorgesPrisRateRepository store = Store(NorgesPrisRates.Rate("2027-01-01", 0.50m));

        // 23:30 UTC on 31 December is already 1 January in Oslo.
        Result<IReadOnlyList<NorgesPrisRateResponse>> late =
            await ListHandler(store, "2026-12-31T23:30:00Z").Handle(new ListNorgesPrisRatesQuery(Admin), CancellationToken.None);
        Result<IReadOnlyList<NorgesPrisRateResponse>> early =
            await ListHandler(store, "2026-12-31T22:30:00Z").Handle(new ListNorgesPrisRatesQuery(Admin), CancellationToken.None);

        Assert.Equal("InEffect", late.Value[1].Status);
        Assert.Equal("Upcoming", early.Value[1].Status);
    }

    // ---- add --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Add_AFutureRate_IsSavedAndReturnedAsUpcoming()
    {
        FakeNorgesPrisRateRepository store = Store();

        Result<NorgesPrisRateResponse> result = await AddHandler(store).Handle(Add("2027-01-01"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Upcoming", result.Value.Status);
        Assert.Equal(0.50m, result.Value.RatePerKwh);
        Assert.Equal(2, store.Stored.Count);
        Assert.Equal(Admin, store.Stored[1].AddedByUserId);
    }

    [Fact]
    public async Task Add_AFutureRateNeedsNoConfirmation_AndMayBeEarlierThanAnotherFutureRate()
    {
        FakeNorgesPrisRateRepository store = Store(NorgesPrisRates.Rate("2027-01-01", 0.50m));

        Result<NorgesPrisRateResponse> result = await AddHandler(store).Handle(Add("2026-12-01", 0.45m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, store.Stored.Count);
    }

    [Fact]
    public async Task Add_AnExistingDate_IsRefusedAndNothingIsSaved()
    {
        FakeNorgesPrisRateRepository store = Store(NorgesPrisRates.Rate("2027-01-01", 0.50m));

        Result<NorgesPrisRateResponse> result = await AddHandler(store).Handle(Add("2027-01-01", 0.60m), CancellationToken.None);

        Assert.Equal("NorgesPris.DateExists", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(2, store.Stored.Count);
    }

    [Fact]
    public async Task Add_WhenAConcurrentRequestTookTheDate_IsRefusedAsDateExists()
    {
        FakeNorgesPrisRateRepository store = Store();
        store.LoseTheRace = true;

        Result<NorgesPrisRateResponse> result = await AddHandler(store).Handle(Add("2027-01-01"), CancellationToken.None);

        Assert.Equal("NorgesPris.DateExists", result.Error.Code);
    }

    [Fact]
    public async Task Add_AStartDateOfTodayAsTheNewest_NeedsConfirmation()
    {
        FakeNorgesPrisRateRepository store = Store();

        Result<NorgesPrisRateResponse> unconfirmed = await AddHandler(store).Handle(Add("2026-11-13"), CancellationToken.None);
        Result<NorgesPrisRateResponse> confirmed =
            await AddHandler(store).Handle(Add("2026-11-13", confirm: true), CancellationToken.None);

        Assert.Equal("NorgesPris.ConfirmationRequired", unconfirmed.Error.Code);
        Assert.Equal(ErrorType.Conflict, unconfirmed.Error.Type);
        Assert.True(confirmed.IsSuccess);
        Assert.Equal("InEffect", confirmed.Value.Status);
    }

    [Fact]
    public async Task Add_APastDateAsTheNewest_NeedsConfirmationAndThenWorks()
    {
        FakeNorgesPrisRateRepository store = Store();

        Result<NorgesPrisRateResponse> unconfirmed = await AddHandler(store).Handle(Add("2026-11-12"), CancellationToken.None);
        Result<NorgesPrisRateResponse> confirmed =
            await AddHandler(store).Handle(Add("2026-11-12", confirm: true), CancellationToken.None);

        Assert.Equal("NorgesPris.ConfirmationRequired", unconfirmed.Error.Code);
        Assert.True(confirmed.IsSuccess);
        Assert.Equal(2, store.Stored.Count);
    }

    [Fact]
    public async Task Add_APastDateThatIsNotTheNewest_IsRefusedEvenWhenConfirmed()
    {
        FakeNorgesPrisRateRepository store = Store(NorgesPrisRates.Rate("2026-11-01", 0.45m));

        Result<NorgesPrisRateResponse> result =
            await AddHandler(store).Handle(Add("2026-10-01", confirm: true), CancellationToken.None);

        Assert.Equal("NorgesPris.NotNewest", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(2, store.Stored.Count);
    }

    [Fact]
    public async Task Add_TheConfirmationFlag_IsIgnoredForAFutureDate()
    {
        FakeNorgesPrisRateRepository store = Store();

        Result<NorgesPrisRateResponse> result = await AddHandler(store).Handle(Add("2027-06-01", confirm: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Add_ADuplicateIsReportedBeforeAMissingConfirmation()
    {
        FakeNorgesPrisRateRepository store = Store(NorgesPrisRates.Rate("2026-11-12", 0.45m));

        Result<NorgesPrisRateResponse> result = await AddHandler(store).Handle(Add("2026-11-12"), CancellationToken.None);

        Assert.Equal("NorgesPris.DateExists", result.Error.Code);
    }

    // ---- delete -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_AnUpcomingRate_IsRemovedAndSaved()
    {
        NorgesPrisRate upcoming = NorgesPrisRates.Rate("2027-01-01", 0.50m);
        FakeNorgesPrisRateRepository store = Store(upcoming);

        Result<NorgesPrisRateDeleted> result =
            await DeleteHandler(store).Handle(new DeleteNorgesPrisRateCommand(upcoming.Id, Admin), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(store.Stored);
        Assert.Equal(1, store.SaveCalls);
    }

    [Theory]
    [InlineData("2026-11-13")] // starts today
    [InlineData("2026-06-01")] // in effect
    public async Task Delete_ARateThatHasStarted_IsRefusedAndNothingIsRemoved(string validFrom)
    {
        NorgesPrisRate started = NorgesPrisRates.Rate(validFrom, 0.45m);
        FakeNorgesPrisRateRepository store = Store(started);

        Result<NorgesPrisRateDeleted> result =
            await DeleteHandler(store).Handle(new DeleteNorgesPrisRateCommand(started.Id, Admin), CancellationToken.None);

        Assert.Equal("NorgesPris.AlreadyStarted", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(2, store.Stored.Count);
    }

    [Fact]
    public async Task Delete_TheSeededRateWhichIsTheOnlyRate_IsRefused()
    {
        FakeNorgesPrisRateRepository store = Store();

        Result<NorgesPrisRateDeleted> result =
            await DeleteHandler(store).Handle(new DeleteNorgesPrisRateCommand(_seeded.Id, Admin), CancellationToken.None);

        Assert.Equal("NorgesPris.AlreadyStarted", result.Error.Code);
        Assert.Single(store.Stored);
    }

    [Fact]
    public async Task Delete_AnUnknownRate_IsNotFound()
    {
        FakeNorgesPrisRateRepository store = Store();

        Result<NorgesPrisRateDeleted> result =
            await DeleteHandler(store).Handle(new DeleteNorgesPrisRateCommand(Guid.NewGuid(), Admin), CancellationToken.None);

        Assert.Equal("NorgesPris.NotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task Delete_TheNewestUpcomingRate_LetsThePreviousRatePriceTheHoursAgain()
    {
        FakeNorgesPrisRateRepository store = Store();
        Result<NorgesPrisRateResponse> added = await AddHandler(store).Handle(Add("2027-01-01"), CancellationToken.None);
        Func<CostCalculator> calculator = () => new CostCalculator(
            Substitute.For<Application.Common.Interfaces.Repositories.IElectricityPriceRepository<ElectricityPrice>>(),
            Substitute.For<Application.Common.Interfaces.Repositories.IExchangeRateRepository<ExchangeRate>>(),
            store);
        var location = new Location(Guid.NewGuid(), "Home", "Addr", "SN1", "NO1", true, true);
        var hour = new DateTime(2027, 2, 1, 10, 0, 0, DateTimeKind.Utc);

        CostResult withNewRate = await calculator().CalculateAsync(location, hour, 1m, CancellationToken.None);
        await DeleteHandler(store).Handle(new DeleteNorgesPrisRateCommand(added.Value.Id, Admin), CancellationToken.None);
        CostResult afterDelete = await calculator().CalculateAsync(location, hour, 1m, CancellationToken.None);

        Assert.Equal(0.50m, withNewRate.Actual!.RatePerKwh);
        Assert.Equal(0.40m, afterDelete.Actual!.RatePerKwh);
    }
}
