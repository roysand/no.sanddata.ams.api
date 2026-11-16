using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Pricing.Commands;
using Features.Pricing.Handlers;
using Features.Pricing.Queries;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Pricing;

public class LocationPricingHandlerTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly FakePriceRateRepository _rates = new();

    private static Location LocationNamed(string name) =>
        new(Guid.NewGuid(), name, "Addr", "SN-" + name, "NO1", true, false);

    private SetLocationPricingCommandHandler SetHandler() =>
        new(_locations, _rates, Substitute.For<ILogger<SetLocationPricingCommandHandler>>());

    private ListLocationPricingQueryHandler ListHandler() =>
        new(_locations, _rates, Substitute.For<ILogger<ListLocationPricingQueryHandler>>());

    // ---- set --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Set_StoresTheTariffAndTheMarkup_AndSavesOnce()
    {
        Location location = LocationNamed("Home");
        var tariff = new GridTariff(Guid.NewGuid(), "Elvia");
        _rates.Tariffs.Add(tariff);
        _locations.GetByIdAsync(location.Id, Arg.Any<CancellationToken>()).Returns(location);

        Result<LocationPricingResponse> result = await SetHandler()
            .Handle(new SetLocationPricingCommand(location.Id, tariff.Id, 0.0250m, Admin), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(tariff.Id, location.GridTariffId);
        Assert.Equal(0.0250m, location.SupplierMarkupPerKwh);
        Assert.Equal("Elvia", result.Value.GridTariffName);
        Assert.True(result.Value.IsComplete);
        await _locations.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Set_WithNullTariff_ClearsItAndMarksTheLocationIncomplete()
    {
        Location location = LocationNamed("Home");
        location.SetPricing(Guid.NewGuid(), 0.02m);
        _locations.GetByIdAsync(location.Id, Arg.Any<CancellationToken>()).Returns(location);

        Result<LocationPricingResponse> result = await SetHandler()
            .Handle(new SetLocationPricingCommand(location.Id, null, 0m, Admin), CancellationToken.None);

        Assert.Null(location.GridTariffId);
        Assert.False(result.Value.IsComplete);
        Assert.Null(result.Value.GridTariffName);
    }

    [Fact]
    public async Task Set_OnAnUnknownLocation_IsLocationNotFound()
    {
        Result<LocationPricingResponse> result = await SetHandler()
            .Handle(new SetLocationPricingCommand(Guid.NewGuid(), null, 0m, Admin), CancellationToken.None);

        Assert.Equal("Pricing.LocationNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        await _locations.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Set_WithAnUnknownTariff_IsTariffNotFound_AndChangesNothing()
    {
        Location location = LocationNamed("Home");
        _locations.GetByIdAsync(location.Id, Arg.Any<CancellationToken>()).Returns(location);

        Result<LocationPricingResponse> result = await SetHandler()
            .Handle(new SetLocationPricingCommand(location.Id, Guid.NewGuid(), 0.02m, Admin), CancellationToken.None);

        Assert.Equal("Pricing.TariffNotFound", result.Error.Code);
        Assert.Null(location.GridTariffId);
        Assert.Equal(0m, location.SupplierMarkupPerKwh);
        await _locations.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- list (user story 4) ----------------------------------------------------------------------------------

    [Fact]
    public async Task List_MarksLocationsWithoutATariffIncomplete_AndShowsTheOthers()
    {
        var tariff = new GridTariff(Guid.NewGuid(), "Elvia");
        _rates.Tariffs.Add(tariff);
        Location configured = LocationNamed("Cabin");
        configured.SetPricing(tariff.Id, 0.0300m);
        Location bare = LocationNamed("Home");
        _locations.GetAllWithKeyAsync(Arg.Any<CancellationToken>()).Returns([bare, configured]);

        Result<IReadOnlyList<LocationPricingResponse>> result =
            await ListHandler().Handle(new ListLocationPricingQuery(Admin), CancellationToken.None);

        Assert.Equal(["Cabin", "Home"], result.Value.Select(l => l.Name));
        LocationPricingResponse cabin = result.Value[0];
        Assert.True(cabin.IsComplete);
        Assert.Equal("Elvia", cabin.GridTariffName);
        Assert.Equal(0.0300m, cabin.SupplierMarkupPerKwh);
        LocationPricingResponse home = result.Value[1];
        Assert.False(home.IsComplete);
        Assert.Null(home.GridTariffId);
        Assert.Equal(0m, home.SupplierMarkupPerKwh);
    }

    [Fact]
    public async Task List_ReturnsEveryLocation_IncludingOnesTheAdminIsNotLinkedTo()
    {
        _locations.GetAllWithKeyAsync(Arg.Any<CancellationToken>())
            .Returns([LocationNamed("A"), LocationNamed("B"), LocationNamed("C")]);

        Result<IReadOnlyList<LocationPricingResponse>> result =
            await ListHandler().Handle(new ListLocationPricingQuery(Admin), CancellationToken.None);

        Assert.Equal(3, result.Value.Count);
    }
}
