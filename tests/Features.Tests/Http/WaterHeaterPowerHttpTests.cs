using System.Net;
using System.Text.Json;
using Domain.Common;
using Features.WaterHeaters.Queries;
using NSubstitute;

namespace Features.Tests.Http;

/// <summary>The real pipeline for the heater's 5-minute power: sign-in, binding, validation, JSON shape, errors.</summary>
public class WaterHeaterPowerHttpTests
{
    private static readonly Guid LocationId = Guid.NewGuid();
    private static readonly string Url = $"/api/locations/{LocationId}/water-heater/power";

    private static readonly DateTime From = new(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);

    private static readonly WaterHeaterPowerResponse Power = new(
        LocationId, From, From.AddHours(3), 300,
        [
            new WaterHeaterPowerItem(From, 0.012m, 300),
            new WaterHeaterPowerItem(From.AddMinutes(175), 2.018m, 134)
        ]);

    private static Task<EndpointHost> StartHost(HostCaller caller = HostCaller.User, Error? error = null) =>
        EndpointHost.StartAsync(
            dispatcher => dispatcher.Send(Arg.Any<GetWaterHeaterPowerQuery>(), Arg.Any<CancellationToken>())
                .Returns(error is null ? Result.Success(Power) : Result.Failure<WaterHeaterPowerResponse>(error)),
            caller);

    [Fact]
    public async Task TheWindow_IsInTheContractShape()
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await host.Client.GetAsync(Url);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = json.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LocationId, root.GetProperty("locationId").GetGuid());
        Assert.Equal(From, root.GetProperty("from").GetDateTime().ToUniversalTime());
        Assert.Equal(From.AddHours(3), root.GetProperty("to").GetDateTime().ToUniversalTime());
        Assert.Equal(300, root.GetProperty("stepSeconds").GetInt32());
        JsonElement last = root.GetProperty("items")[1];
        Assert.Equal(From.AddMinutes(175), last.GetProperty("start").GetDateTime().ToUniversalTime());
        Assert.Equal(2.018m, last.GetProperty("powerKw").GetDecimal());
        Assert.Equal(134, last.GetProperty("sampledSeconds").GetInt32());
    }

    [Fact]
    public async Task FromAndTo_ArePassedOn_ForAUser()
    {
        await using EndpointHost host = await StartHost();

        await host.Client.GetAsync($"{Url}?from=2026-11-20T09:00:00Z&to=2026-11-20T12:00:00Z");

        await host.Dispatcher.Received(1).Send(
            Arg.Is<GetWaterHeaterPowerQuery>(q => q.LocationId == LocationId && !q.IsAdmin
                                                  && q.From!.Value.ToUniversalTime() == From
                                                  && q.To!.Value.ToUniversalTime() == From.AddHours(3)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NothingGiven_IsPassedOnAsLeftOut_ForAnAdmin()
    {
        await using EndpointHost host = await StartHost(HostCaller.Admin);

        await host.Client.GetAsync(Url);

        await host.Dispatcher.Received(1).Send(
            Arg.Is<GetWaterHeaterPowerQuery>(q => q.IsAdmin && q.From == null && q.To == null),
            Arg.Any<CancellationToken>());
    }

    public static TheoryData<string> BadWindows()
    {
        DateTime tomorrow = DateTime.UtcNow.Date.AddDays(1);
        return new TheoryData<string>
        {
            "?from=2026-11-20T12:00:00Z&to=2026-11-20T09:00:00Z",
            "?from=2026-11-18T11:00:00Z&to=2026-11-20T12:00:00Z",
            $"?from={tomorrow:yyyy-MM-ddTHH:mm:ssZ}&to={tomorrow.AddHours(3):yyyy-MM-ddTHH:mm:ssZ}"
        };
    }

    [Theory]
    [MemberData(nameof(BadWindows))]
    public async Task ABadWindow_IsA400_BeforeTheHandler(string query)
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await host.Client.GetAsync(Url + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await host.Dispatcher.DidNotReceive().Send(
            Arg.Any<GetWaterHeaterPowerQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotSignedIn_IsA401()
    {
        await using EndpointHost host = await StartHost(HostCaller.Anonymous);

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task LocationNotFound_IsA404()
    {
        await using EndpointHost host =
            await StartHost(error: Error.NotFound("Location.NotFound", "Location not found"));

        HttpResponseMessage response = await host.Client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Location not found", await response.Content.ReadAsStringAsync());
    }
}
