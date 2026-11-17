using Domain.Common;
using Domain.Common.Entities;
using Features.Pricing.Commands;
using Features.Pricing.Handlers;
using Features.Pricing.Queries;
using Features.Tests.NorgesPris;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Pricing;

public class PriceRateHandlerTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    // 2026-11-16 12:00 UTC is 13:00 in Oslo on the same day.
    private const string Now = "2026-11-16T12:00:00Z";

    private static PriceRate Rate(PriceRateKind kind, string validFrom, decimal value) =>
        new(Guid.NewGuid(), kind, DateOnly.Parse(validFrom), value, Guid.Empty);

    private static FakePriceRateRepository Store(params PriceRate[] rates)
    {
        var store = new FakePriceRateRepository();
        store.PriceRates.AddRange(rates);
        return store;
    }

    private static AddPriceRateCommandHandler AddHandler(FakePriceRateRepository store, string now = Now) =>
        new(store, new FixedTimeProvider(now), Substitute.For<ILogger<AddPriceRateCommandHandler>>());

    private static DeletePriceRateCommandHandler DeleteHandler(FakePriceRateRepository store, string now = Now) =>
        new(store, new FixedTimeProvider(now), Substitute.For<ILogger<DeletePriceRateCommandHandler>>());

    private static ListPriceRatesQueryHandler ListHandler(FakePriceRateRepository store, string now = Now) =>
        new(store, new FixedTimeProvider(now), Substitute.For<ILogger<ListPriceRatesQueryHandler>>());

    private static AddPriceRateCommand Add(
        PriceRateKind kind, string validFrom, decimal value = 0.08m, bool confirm = false) =>
        new(kind, DateOnly.Parse(validFrom), value, confirm, Admin);

    // ---- list -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task List_MarksOneRatePerKindInEffect_AndTheFutureOnesUpcoming()
    {
        FakePriceRateRepository store = Store(
            Rate(PriceRateKind.ElectricityTax, "2025-01-01", 0.09m),
            Rate(PriceRateKind.ElectricityTax, "2026-01-01", 0.0713m),
            Rate(PriceRateKind.ElectricityTax, "2027-01-01", 0.08m),
            Rate(PriceRateKind.Vat, "2020-01-01", 0.25m));

        Result<IReadOnlyList<PriceRateResponse>> result =
            await ListHandler(store).Handle(new ListPriceRatesQuery(null, Admin), CancellationToken.None);

        PriceRateResponse[] tax = result.Value.Where(r => r.Kind == "ElectricityTax").ToArray();
        Assert.Equal(["Superseded", "InEffect", "Upcoming"], tax.Select(r => r.Status));
        Assert.Equal("InEffect", result.Value.Single(r => r.Kind == "Vat").Status);
    }

    [Fact]
    public async Task List_CanBeFilteredByKind()
    {
        FakePriceRateRepository store = Store(
            Rate(PriceRateKind.ElectricityTax, "2026-01-01", 0.0713m),
            Rate(PriceRateKind.EnovaFee, "2026-01-01", 0.01m));

        Result<IReadOnlyList<PriceRateResponse>> result = await ListHandler(store)
            .Handle(new ListPriceRatesQuery(PriceRateKind.EnovaFee, Admin), CancellationToken.None);

        Assert.Equal("EnovaFee", Assert.Single(result.Value).Kind);
    }

    [Fact]
    public async Task List_UsesTheOsloDateNotTheUtcDate()
    {
        FakePriceRateRepository store = Store(Rate(PriceRateKind.ElectricityTax, "2027-01-01", 0.08m));

        // 23:30 UTC on 31 December is already 1 January in Oslo.
        Result<IReadOnlyList<PriceRateResponse>> late = await ListHandler(store, "2026-12-31T23:30:00Z")
            .Handle(new ListPriceRatesQuery(null, Admin), CancellationToken.None);
        Result<IReadOnlyList<PriceRateResponse>> early = await ListHandler(store, "2026-12-31T22:30:00Z")
            .Handle(new ListPriceRatesQuery(null, Admin), CancellationToken.None);

        Assert.Equal("InEffect", late.Value[0].Status);
        Assert.Equal("Upcoming", early.Value[0].Status);
    }

    // ---- add --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Add_AFutureRate_IsSavedAndReturnedAsUpcoming()
    {
        FakePriceRateRepository store = Store(Rate(PriceRateKind.ElectricityTax, "2026-01-01", 0.0713m));

        Result<PriceRateResponse> result = await AddHandler(store)
            .Handle(Add(PriceRateKind.ElectricityTax, "2027-01-01"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Upcoming", result.Value.Status);
        Assert.Equal("ElectricityTax", result.Value.Kind);
        Assert.Equal(2, store.PriceRates.Count);
        Assert.Equal(Admin, store.PriceRates[1].AddedByUserId);
    }

    [Fact]
    public async Task Add_TheSameDateForAnotherKind_IsAccepted()
    {
        FakePriceRateRepository store = Store(Rate(PriceRateKind.ElectricityTax, "2027-01-01", 0.08m));

        Result<PriceRateResponse> result = await AddHandler(store)
            .Handle(Add(PriceRateKind.EnovaFee, "2027-01-01", 0.01m), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Add_AnExistingDateForTheSameKind_IsRefusedAndNothingIsSaved()
    {
        FakePriceRateRepository store = Store(Rate(PriceRateKind.ElectricityTax, "2027-01-01", 0.08m));

        Result<PriceRateResponse> result = await AddHandler(store)
            .Handle(Add(PriceRateKind.ElectricityTax, "2027-01-01", 0.09m), CancellationToken.None);

        Assert.Equal("Pricing.DateExists", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Single(store.PriceRates);
    }

    [Fact]
    public async Task Add_WhenAConcurrentRequestTookTheDate_IsRefusedAsDateExists()
    {
        FakePriceRateRepository store = Store();
        store.LoseTheRace = true;

        Result<PriceRateResponse> result = await AddHandler(store)
            .Handle(Add(PriceRateKind.Vat, "2027-01-01", 0.25m), CancellationToken.None);

        Assert.Equal("Pricing.DateExists", result.Error.Code);
    }

    [Fact]
    public async Task Add_AStartDateOfTodayAsTheNewest_NeedsConfirmation()
    {
        FakePriceRateRepository store = Store(Rate(PriceRateKind.ElectricityTax, "2026-01-01", 0.0713m));

        Result<PriceRateResponse> unconfirmed = await AddHandler(store)
            .Handle(Add(PriceRateKind.ElectricityTax, "2026-11-16"), CancellationToken.None);
        Result<PriceRateResponse> confirmed = await AddHandler(store)
            .Handle(Add(PriceRateKind.ElectricityTax, "2026-11-16", confirm: true), CancellationToken.None);

        Assert.Equal("Pricing.ConfirmationRequired", unconfirmed.Error.Code);
        Assert.True(confirmed.IsSuccess);
        Assert.Equal("InEffect", confirmed.Value.Status);
    }

    [Fact]
    public async Task Add_APastDateThatIsNotTheNewestOfItsKind_IsRefusedEvenWhenConfirmed()
    {
        FakePriceRateRepository store = Store(
            Rate(PriceRateKind.ElectricityTax, "2026-01-01", 0.0713m),
            Rate(PriceRateKind.ElectricityTax, "2026-06-01", 0.075m));

        Result<PriceRateResponse> result = await AddHandler(store)
            .Handle(Add(PriceRateKind.ElectricityTax, "2026-03-01", confirm: true), CancellationToken.None);

        Assert.Equal("Pricing.NotNewest", result.Error.Code);
        Assert.Equal(2, store.PriceRates.Count);
    }

    [Fact]
    public async Task Add_APastDateIsJudgedAgainstItsOwnKindOnly()
    {
        FakePriceRateRepository store = Store(
            Rate(PriceRateKind.ElectricityTax, "2026-06-01", 0.0713m),
            Rate(PriceRateKind.EnovaFee, "2026-01-01", 0.01m));

        // Newer than every Enova row even though older than a tax row.
        Result<PriceRateResponse> result = await AddHandler(store)
            .Handle(Add(PriceRateKind.EnovaFee, "2026-03-01", 0.011m, confirm: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Add_AFutureDate_IgnoresTheConfirmationFlag()
    {
        FakePriceRateRepository store = Store(Rate(PriceRateKind.ElectricityTax, "2026-01-01", 0.0713m));

        Result<PriceRateResponse> result = await AddHandler(store)
            .Handle(Add(PriceRateKind.ElectricityTax, "2027-06-01", confirm: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Add_ADuplicateIsReportedBeforeAMissingConfirmation()
    {
        FakePriceRateRepository store = Store(Rate(PriceRateKind.Vat, "2026-11-15", 0.25m));

        Result<PriceRateResponse> result = await AddHandler(store)
            .Handle(Add(PriceRateKind.Vat, "2026-11-15", 0.25m), CancellationToken.None);

        Assert.Equal("Pricing.DateExists", result.Error.Code);
    }

    // ---- delete -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_AnUpcomingRate_IsRemovedAndSavedOnce()
    {
        PriceRate upcoming = Rate(PriceRateKind.ElectricityTax, "2027-01-01", 0.08m);
        FakePriceRateRepository store = Store(Rate(PriceRateKind.ElectricityTax, "2026-01-01", 0.0713m), upcoming);

        Result<PricingDeleted> result =
            await DeleteHandler(store).Handle(new DeletePriceRateCommand(upcoming.Id, Admin), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(store.PriceRates);
        Assert.Equal(1, store.SaveCalls);
    }

    [Theory]
    [InlineData("2026-11-16")] // starts today
    [InlineData("2026-01-01")] // in effect
    public async Task Delete_ARateThatHasStarted_IsRefusedAndNothingIsRemoved(string validFrom)
    {
        PriceRate started = Rate(PriceRateKind.ElectricityTax, validFrom, 0.0713m);
        FakePriceRateRepository store = Store(started);

        Result<PricingDeleted> result =
            await DeleteHandler(store).Handle(new DeletePriceRateCommand(started.Id, Admin), CancellationToken.None);

        Assert.Equal("Pricing.AlreadyStarted", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Single(store.PriceRates);
    }

    [Fact]
    public async Task Delete_AnUnknownRate_IsNotFound()
    {
        Result<PricingDeleted> result = await DeleteHandler(Store())
            .Handle(new DeletePriceRateCommand(Guid.NewGuid(), Admin), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    // ---- the policy on its own --------------------------------------------------------------------------------

    [Fact]
    public void Policy_AllowsAFutureDateAndRefusesTheOthersInTheDocumentedOrder()
    {
        DateOnly today = new(2026, 11, 16);
        DateOnly[] existing = [new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 1)];

        Assert.Null(DatedRatePolicy.Check(existing, new DateOnly(2027, 1, 1), false, today));
        Assert.Equal("Pricing.DateExists", DatedRatePolicy.Check(existing, existing[1], true, today)!.Code);
        Assert.Equal("Pricing.NotNewest", DatedRatePolicy.Check(existing, new DateOnly(2026, 3, 1), true, today)!.Code);
        Assert.Equal(
            "Pricing.ConfirmationRequired",
            DatedRatePolicy.Check(existing, new DateOnly(2026, 11, 1), false, today)!.Code);
        Assert.Null(DatedRatePolicy.Check(existing, new DateOnly(2026, 11, 1), true, today));
        Assert.Null(DatedRatePolicy.Check([], new DateOnly(2020, 1, 1), true, today));
    }
}
