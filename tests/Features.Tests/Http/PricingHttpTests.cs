using System.Net;
using System.Text;
using Domain.Common;
using Features.Pricing.Commands;
using Features.Pricing.Queries;
using NSubstitute;

namespace Features.Tests.Http;

/// <summary>The real pipeline for the Pricing endpoints: who may call them, and how a request is bound.</summary>
public class PricingHttpTests
{
    private static readonly Guid RateId = Guid.NewGuid();
    private static readonly Guid TariffId = Guid.NewGuid();
    private static readonly Guid LocationId = Guid.NewGuid();

    private static readonly PriceRateResponse PriceRate = new(RateId, "ElectricityTax", new DateOnly(2027, 1, 1), 0.08m, "Upcoming");
    private static readonly GridTariffRateResponse TariffRate = new(RateId, new DateOnly(2027, 1, 1), 0.35m, 0.22m, "Upcoming");
    private static readonly GridTariffResponse Tariff = new(TariffId, "Elvia", [TariffRate]);
    private static readonly LocationPricingResponse LocationPricing = new(LocationId, "Home", TariffId, "Elvia", 0.02m, true);

    private static Task<EndpointHost> StartHost(HostCaller caller = HostCaller.Admin) =>
        EndpointHost.StartAsync(
            dispatcher =>
            {
                dispatcher.Send(Arg.Any<ListPriceRatesQuery>(), Arg.Any<CancellationToken>())
                    .Returns(Result.Success<IReadOnlyList<PriceRateResponse>>([PriceRate]));
                dispatcher.Send(Arg.Any<AddPriceRateCommand>(), Arg.Any<CancellationToken>())
                    .Returns(Result.Success(PriceRate));
                dispatcher.Send(Arg.Any<DeletePriceRateCommand>(), Arg.Any<CancellationToken>())
                    .Returns(Result.Success(new PricingDeleted(RateId)));
                dispatcher.Send(Arg.Any<ListGridTariffsQuery>(), Arg.Any<CancellationToken>())
                    .Returns(Result.Success<IReadOnlyList<GridTariffResponse>>([Tariff]));
                dispatcher.Send(Arg.Any<CreateGridTariffCommand>(), Arg.Any<CancellationToken>())
                    .Returns(Result.Success(Tariff));
                dispatcher.Send(Arg.Any<AddGridTariffRateCommand>(), Arg.Any<CancellationToken>())
                    .Returns(Result.Success(TariffRate));
                dispatcher.Send(Arg.Any<DeleteGridTariffRateCommand>(), Arg.Any<CancellationToken>())
                    .Returns(Result.Success(new PricingDeleted(RateId)));
                dispatcher.Send(Arg.Any<ListLocationPricingQuery>(), Arg.Any<CancellationToken>())
                    .Returns(Result.Success<IReadOnlyList<LocationPricingResponse>>([LocationPricing]));
                dispatcher.Send(Arg.Any<SetLocationPricingCommand>(), Arg.Any<CancellationToken>())
                    .Returns(Result.Success(LocationPricing));
            },
            caller);

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private const string PriceRateBody = """{ "kind": "ElectricityTax", "validFrom": "2027-01-01", "value": 0.08 }""";
    private const string TariffRateBody = """{ "validFrom": "2027-01-01", "dayRatePerKwh": 0.35, "nightRatePerKwh": 0.22 }""";
    private const string LocationBody = """{ "gridTariffId": null, "supplierMarkupPerKwh": 0.02 }""";

    /// <summary>Every Pricing endpoint with a valid request and the status an Admin gets.</summary>
    public static TheoryData<string, string, string?, HttpStatusCode> Endpoints => new()
    {
        { "GET", "/api/admin/price-rates", null, HttpStatusCode.OK },
        { "POST", "/api/admin/price-rates", PriceRateBody, HttpStatusCode.Created },
        { "DELETE", $"/api/admin/price-rates/{RateId}", null, HttpStatusCode.NoContent },
        { "GET", "/api/admin/grid-tariffs", null, HttpStatusCode.OK },
        { "POST", "/api/admin/grid-tariffs", """{ "name": "Elvia" }""", HttpStatusCode.Created },
        { "POST", $"/api/admin/grid-tariffs/{TariffId}/rates", TariffRateBody, HttpStatusCode.Created },
        { "DELETE", $"/api/admin/grid-tariffs/{TariffId}/rates/{RateId}", null, HttpStatusCode.NoContent },
        { "GET", "/api/admin/location-pricing", null, HttpStatusCode.OK },
        { "PUT", $"/api/admin/location-pricing/{LocationId}", LocationBody, HttpStatusCode.OK }
    };

    private static Task<HttpResponseMessage> Send(EndpointHost host, string method, string url, string? body) =>
        host.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = body is null ? null : Json(body)
        });

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task EveryEndpoint_GivesAnAdminTheSuccessStatus(string method, string url, string? body, HttpStatusCode ok)
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await Send(host, method, url, body);

        Assert.Equal(ok, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task EveryEndpoint_IsRefusedWithoutASignIn(string method, string url, string? body, HttpStatusCode _)
    {
        await using EndpointHost host = await StartHost(HostCaller.Anonymous);

        HttpResponseMessage response = await Send(host, method, url, body);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task EveryEndpoint_IsRefusedForAnOrdinaryUser(string method, string url, string? body, HttpStatusCode _)
    {
        await using EndpointHost host = await StartHost(HostCaller.User);

        HttpResponseMessage response = await Send(host, method, url, body);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static T? Received<T>(EndpointHost host) where T : class =>
        host.Dispatcher.ReceivedCalls().Select(call => call.GetArguments()[0]).OfType<T>().SingleOrDefault();

    // ---- binding ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task AddPriceRate_BindsTheKindFromTextAndTheConfirmationDefaultsToFalse()
    {
        await using EndpointHost host = await StartHost();

        await host.Client.PostAsync("/api/admin/price-rates", Json(PriceRateBody));

        AddPriceRateCommand command = Received<AddPriceRateCommand>(host)!;
        Assert.Equal(PriceRateKind.ElectricityTax, command.Kind);
        Assert.Equal(new DateOnly(2027, 1, 1), command.ValidFrom);
        Assert.Equal(0.08m, command.Value);
        Assert.False(command.ConfirmPastDate);
    }

    [Fact]
    public async Task AddPriceRate_AcceptsTheKindInAnyCase_AndTheConfirmation()
    {
        await using EndpointHost host = await StartHost();

        await host.Client.PostAsync("/api/admin/price-rates", Json(
            """{ "kind": "vat", "validFrom": "2026-10-01", "value": 0.25, "confirmPastDate": true }"""));

        AddPriceRateCommand command = Received<AddPriceRateCommand>(host)!;
        Assert.Equal(PriceRateKind.Vat, command.Kind);
        Assert.True(command.ConfirmPastDate);
    }

    [Theory]
    [InlineData("""{ "kind": "Tax", "validFrom": "2027-01-01", "value": 0.08 }""")]
    [InlineData("""{ "kind": "1", "validFrom": "2027-01-01", "value": 0.08 }""")]
    [InlineData("""{ "kind": 1, "validFrom": "2027-01-01", "value": 0.08 }""")]
    [InlineData("""{ "validFrom": "2027-01-01", "value": 0.08 }""")]
    [InlineData("""{ "kind": "Vat", "validFrom": "2027-01-01", "value": 25 }""")]
    [InlineData("""{ "kind": "Vat", "validFrom": "2027-01-01", "value": -0.1 }""")]
    [InlineData("""{ "kind": "EnovaFee", "validFrom": "2027-01-01", "value": 0.012345 }""")]
    [InlineData("""{ "kind": "EnovaFee", "validFrom": "not a date", "value": 0.01 }""")]
    [InlineData("""{ "kind": "EnovaFee", "value": 0.01 }""")]
    public async Task AddPriceRate_AnInvalidBody_IsRefusedBeforeAnyHandlerRuns(string body)
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await host.Client.PostAsync("/api/admin/price-rates", Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(Received<AddPriceRateCommand>(host));
    }

    [Fact]
    public async Task ListPriceRates_ABadKindIsRefused_AGoodKindIsPassedOn()
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage bad = await host.Client.GetAsync("/api/admin/price-rates?kind=Tax");
        HttpResponseMessage good = await host.Client.GetAsync("/api/admin/price-rates?kind=enovafee");

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
        Assert.Equal(PriceRateKind.EnovaFee, Received<ListPriceRatesQuery>(host)!.Kind);
    }

    [Fact]
    public async Task AddGridTariffRate_BindsTheTariffFromTheRoute_AndTheRatesFromTheBody()
    {
        await using EndpointHost host = await StartHost();

        await host.Client.PostAsync($"/api/admin/grid-tariffs/{TariffId}/rates", Json(TariffRateBody));

        AddGridTariffRateCommand command = Received<AddGridTariffRateCommand>(host)!;
        Assert.Equal(TariffId, command.GridTariffId);
        Assert.Equal(new DateOnly(2027, 1, 1), command.ValidFrom);
        Assert.Equal(0.35m, command.DayRatePerKwh);
        Assert.Equal(0.22m, command.NightRatePerKwh);
        Assert.False(command.ConfirmPastDate);
    }

    [Theory]
    [InlineData("""{ "validFrom": "2027-01-01", "dayRatePerKwh": -1, "nightRatePerKwh": 0.2 }""")]
    [InlineData("""{ "validFrom": "2027-01-01", "dayRatePerKwh": 0.3, "nightRatePerKwh": 0.200001 }""")]
    [InlineData("""{ "dayRatePerKwh": 0.3, "nightRatePerKwh": 0.2 }""")]
    public async Task AddGridTariffRate_AnInvalidBody_IsRefusedBeforeAnyHandlerRuns(string body)
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await host.Client.PostAsync(
            $"/api/admin/grid-tariffs/{TariffId}/rates", Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(Received<AddGridTariffRateCommand>(host));
    }

    [Fact]
    public async Task CreateGridTariff_AnEmptyName_IsRefused()
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await host.Client.PostAsync("/api/admin/grid-tariffs", Json("""{ "name": "" }"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(Received<CreateGridTariffCommand>(host));
    }

    [Fact]
    public async Task DeleteGridTariffRate_BindsBothIdsFromTheRoute()
    {
        await using EndpointHost host = await StartHost();

        await host.Client.DeleteAsync($"/api/admin/grid-tariffs/{TariffId}/rates/{RateId}");

        DeleteGridTariffRateCommand command = Received<DeleteGridTariffRateCommand>(host)!;
        Assert.Equal(TariffId, command.GridTariffId);
        Assert.Equal(RateId, command.RateId);
    }

    [Fact]
    public async Task SetLocationPricing_BindsTheLocationFromTheRoute_AndAcceptsANullTariff()
    {
        await using EndpointHost host = await StartHost();

        await host.Client.PutAsync($"/api/admin/location-pricing/{LocationId}", Json(LocationBody));
        await host.Client.PutAsync(
            $"/api/admin/location-pricing/{LocationId}",
            Json($$"""{ "gridTariffId": "{{TariffId}}", "supplierMarkupPerKwh": 0.03 }"""));

        SetLocationPricingCommand[] commands = host.Dispatcher.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<SetLocationPricingCommand>()
            .ToArray();
        Assert.Equal(2, commands.Length);
        Assert.Equal(LocationId, commands[0].LocationId);
        Assert.Null(commands[0].GridTariffId);
        Assert.Equal(0.02m, commands[0].SupplierMarkupPerKwh);
        Assert.Equal(TariffId, commands[1].GridTariffId);
    }

    [Theory]
    [InlineData("""{ "gridTariffId": null, "supplierMarkupPerKwh": -0.01 }""")]
    [InlineData("""{ "gridTariffId": null, "supplierMarkupPerKwh": 100.5 }""")]
    [InlineData("""{ "gridTariffId": null, "supplierMarkupPerKwh": 0.025001 }""")]
    public async Task SetLocationPricing_AnInvalidMarkup_IsRefusedBeforeAnyHandlerRuns(string body)
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await host.Client.PutAsync($"/api/admin/location-pricing/{LocationId}", Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(Received<SetLocationPricingCommand>(host));
    }

    // ---- error mapping ----------------------------------------------------------------------------------------

    [Theory]
    [InlineData(ErrorType.Conflict, HttpStatusCode.Conflict)]
    [InlineData(ErrorType.NotFound, HttpStatusCode.NotFound)]
    public async Task AFailureFromAHandler_MapsToTheMatchingStatus(ErrorType type, HttpStatusCode expected)
    {
        Error error = type == ErrorType.Conflict
            ? Error.Conflict("Pricing.DateExists", "Exists")
            : Error.NotFound("Pricing.TariffNotFound", "Missing");
        await using EndpointHost host = await EndpointHost.StartAsync(dispatcher =>
        {
            dispatcher.Send(Arg.Any<AddPriceRateCommand>(), Arg.Any<CancellationToken>())
                .Returns(Result.Failure<PriceRateResponse>(error));
            dispatcher.Send(Arg.Any<DeleteGridTariffRateCommand>(), Arg.Any<CancellationToken>())
                .Returns(Result.Failure<PricingDeleted>(error));
            dispatcher.Send(Arg.Any<SetLocationPricingCommand>(), Arg.Any<CancellationToken>())
                .Returns(Result.Failure<LocationPricingResponse>(error));
        });

        HttpResponseMessage add = await host.Client.PostAsync("/api/admin/price-rates", Json(PriceRateBody));
        HttpResponseMessage delete = await host.Client.DeleteAsync($"/api/admin/grid-tariffs/{TariffId}/rates/{RateId}");
        HttpResponseMessage set = await host.Client.PutAsync($"/api/admin/location-pricing/{LocationId}", Json(LocationBody));

        Assert.Equal(expected, add.StatusCode);
        Assert.Equal(expected, delete.StatusCode);
        Assert.Equal(expected, set.StatusCode);
    }
}
