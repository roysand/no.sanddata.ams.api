using System.Net;
using System.Net.Http.Json;
using System.Text;
using Domain.Common;
using Features.NorgesPris.Commands;
using Features.NorgesPris.Queries;
using NSubstitute;

namespace Features.Tests.Http;

/// <summary>The real pipeline for the Norgespris endpoints: who may call them, and how a request is bound.</summary>
public class NorgesPrisHttpTests
{
    private const string Url = "/api/admin/norgespris";
    private static readonly Guid RateId = Guid.NewGuid();

    private static readonly NorgesPrisRateResponse Rate =
        new(RateId, new DateOnly(2027, 1, 1), 0.50m, 0m, 0.50m, "Upcoming");

    private static Task<EndpointHost> StartHost(HostCaller caller) =>
        EndpointHost.StartAsync(
            dispatcher =>
            {
                dispatcher
                    .Send(Arg.Any<ListNorgesPrisRatesQuery>(), Arg.Any<CancellationToken>())
                    .Returns(Result.Success<IReadOnlyList<NorgesPrisRateResponse>>([Rate]));
                dispatcher
                    .Send(Arg.Any<AddNorgesPrisRateCommand>(), Arg.Any<CancellationToken>())
                    .Returns(Result.Success(Rate));
                dispatcher
                    .Send(Arg.Any<DeleteNorgesPrisRateCommand>(), Arg.Any<CancellationToken>())
                    .Returns(Result.Success(new NorgesPrisRateDeleted(RateId)));
            },
            caller);

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private const string ValidBody = """{ "validFrom": "2027-01-01", "ratePerKwh": 0.5, "taxPerKwh": 0 }""";

    [Theory]
    [InlineData(HostCaller.Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(HostCaller.User, HttpStatusCode.Forbidden)]
    [InlineData(HostCaller.Admin, HttpStatusCode.OK)]
    public async Task List_IsForAdminsOnly(HostCaller caller, HttpStatusCode expected)
    {
        await using EndpointHost host = await StartHost(caller);

        HttpResponseMessage response = await host.Client.GetAsync(Url);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(HostCaller.Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(HostCaller.User, HttpStatusCode.Forbidden)]
    [InlineData(HostCaller.Admin, HttpStatusCode.Created)]
    public async Task Add_IsForAdminsOnly(HostCaller caller, HttpStatusCode expected)
    {
        await using EndpointHost host = await StartHost(caller);

        HttpResponseMessage response = await host.Client.PostAsync(Url, Json(ValidBody));

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [InlineData(HostCaller.Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(HostCaller.User, HttpStatusCode.Forbidden)]
    [InlineData(HostCaller.Admin, HttpStatusCode.NoContent)]
    public async Task Delete_IsForAdminsOnly(HostCaller caller, HttpStatusCode expected)
    {
        await using EndpointHost host = await StartHost(caller);

        HttpResponseMessage response = await host.Client.DeleteAsync($"{Url}/{RateId}");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Add_BindsTheBodyIntoTheCommand_AndTheConfirmationDefaultsToFalse()
    {
        await using EndpointHost host = await StartHost(HostCaller.Admin);

        await host.Client.PostAsync(Url, Json(ValidBody));
        await host.Client.PostAsync(Url, Json(
            """{ "validFrom": "2026-11-01", "ratePerKwh": 0.45, "taxPerKwh": 0.01, "confirmPastDate": true }"""));

        AddNorgesPrisRateCommand[] commands = host.Dispatcher.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .OfType<AddNorgesPrisRateCommand>()
            .ToArray();
        Assert.Equal(2, commands.Length);
        Assert.Equal(new DateOnly(2027, 1, 1), commands[0].ValidFrom);
        Assert.Equal(0.50m, commands[0].RatePerKwh);
        Assert.False(commands[0].ConfirmPastDate);
        Assert.Equal(new DateOnly(2026, 11, 1), commands[1].ValidFrom);
        Assert.Equal(0.01m, commands[1].TaxPerKwh);
        Assert.True(commands[1].ConfirmPastDate);
    }

    [Theory]
    [InlineData("""{ "validFrom": "2027-01-01", "ratePerKwh": -1, "taxPerKwh": 0 }""")]
    [InlineData("""{ "validFrom": "2027-01-01", "ratePerKwh": 0.12345, "taxPerKwh": 0 }""")]
    [InlineData("""{ "validFrom": "not a date", "ratePerKwh": 0.5, "taxPerKwh": 0 }""")]
    [InlineData("""{ "ratePerKwh": 0.5, "taxPerKwh": 0 }""")]
    public async Task Add_WithAnInvalidBody_IsRefusedBeforeAnyHandlerRuns(string body)
    {
        await using EndpointHost host = await StartHost(HostCaller.Admin);

        HttpResponseMessage response = await host.Client.PostAsync(Url, Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(
            host.Dispatcher.ReceivedCalls().Select(call => call.GetArguments()[0]),
            argument => argument is AddNorgesPrisRateCommand);
    }

    [Fact]
    public async Task Add_AConflictFromTheHandler_Answers409()
    {
        await using EndpointHost host = await EndpointHost.StartAsync(dispatcher =>
            dispatcher
                .Send(Arg.Any<AddNorgesPrisRateCommand>(), Arg.Any<CancellationToken>())
                .Returns(Result.Failure<NorgesPrisRateResponse>(
                    Error.Conflict("NorgesPris.ConfirmationRequired", "Confirm it"))));

        HttpResponseMessage response = await host.Client.PostAsync(Url, Json(ValidBody));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(ErrorType.NotFound, HttpStatusCode.NotFound)]
    [InlineData(ErrorType.Conflict, HttpStatusCode.Conflict)]
    public async Task Delete_AFailureFromTheHandler_MapsToTheMatchingStatus(ErrorType type, HttpStatusCode expected)
    {
        Error error = type == ErrorType.NotFound
            ? Error.NotFound("NorgesPris.NotFound", "Not found")
            : Error.Conflict("NorgesPris.AlreadyStarted", "Started");
        await using EndpointHost host = await EndpointHost.StartAsync(dispatcher =>
            dispatcher
                .Send(Arg.Any<DeleteNorgesPrisRateCommand>(), Arg.Any<CancellationToken>())
                .Returns(Result.Failure<NorgesPrisRateDeleted>(error)));

        HttpResponseMessage response = await host.Client.DeleteAsync($"{Url}/{RateId}");

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsTheRatesAsJson()
    {
        await using EndpointHost host = await StartHost(HostCaller.Admin);

        List<NorgesPrisRateResponse>? rates = await host.Client.GetFromJsonAsync<List<NorgesPrisRateResponse>>(Url);

        Assert.Equal(Rate, Assert.Single(rates!));
    }
}
