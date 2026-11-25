using System.Net;
using System.Text.Json;
using Domain.Common;
using Features.WaterHeaters.Queries;
using NSubstitute;

namespace Features.Tests.Http;

/// <summary>The real pipeline for the hot water used: sign-in, binding, validation, JSON shape, errors (spec 020).</summary>
[Collection(FastEndpointsHostCollection.Name)]
public class WaterHeaterHotWaterHttpTests
{
    private static readonly Guid LocationId = Guid.NewGuid();
    private static readonly string Url = $"/api/locations/{LocationId}/water-heater/hot-water";

    private static readonly DateTime From = new(2026, 11, 20, 23, 0, 0, DateTimeKind.Utc);

    private static readonly WaterHeaterHotWaterResponse HotWater = new(
        LocationId, "day", From, From.AddDays(1), 38, 60,
        [
            new WaterHeaterHotWaterItem(From, null, 0),
            new WaterHeaterHotWaterItem(From.AddHours(8), 38, 60)
        ]);

    private static Task<EndpointHost> StartHost(HostCaller caller = HostCaller.User, Error? error = null) =>
        EndpointHost.StartAsync(
            dispatcher => dispatcher.Send(Arg.Any<GetWaterHeaterHotWaterQuery>(), Arg.Any<CancellationToken>())
                .Returns(error is null
                    ? Result.Success(HotWater)
                    : Result.Failure<WaterHeaterHotWaterResponse>(error)),
            caller);

    [Fact]
    public async Task TheAnswer_IsInTheContractShape()
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await host.Client.GetAsync(Url);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = json.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LocationId, root.GetProperty("locationId").GetGuid());
        Assert.Equal("day", root.GetProperty("period").GetString());
        Assert.Equal(From, root.GetProperty("from").GetDateTime().ToUniversalTime());
        Assert.Equal(From.AddDays(1), root.GetProperty("to").GetDateTime().ToUniversalTime());
        Assert.Equal(38, root.GetProperty("totalLitres").GetInt32());
        Assert.Equal(60, root.GetProperty("sampledMinutes").GetInt32());

        JsonElement gap = root.GetProperty("items")[0];
        Assert.Equal(From, gap.GetProperty("start").GetDateTime().ToUniversalTime());
        Assert.Equal(JsonValueKind.Null, gap.GetProperty("litres").ValueKind);
        Assert.Equal(0, gap.GetProperty("sampledMinutes").GetInt32());

        JsonElement used = root.GetProperty("items")[1];
        Assert.Equal(38, used.GetProperty("litres").GetInt32());
        Assert.Equal(60, used.GetProperty("sampledMinutes").GetInt32());
    }

    [Fact]
    public async Task PeriodAndDate_ArePassedOn_ForAnAdmin()
    {
        await using EndpointHost host = await StartHost(HostCaller.Admin);

        await host.Client.GetAsync($"{Url}?period=Week&date=2026-11-21");

        await host.Dispatcher.Received(1).Send(
            Arg.Is<GetWaterHeaterHotWaterQuery>(q => q.LocationId == LocationId && q.IsAdmin
                                                     && q.Period == HotWaterPeriod.Week
                                                     && q.Date == new DateOnly(2026, 11, 21)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NothingGiven_IsADay_WithNoDate_ForAUser()
    {
        await using EndpointHost host = await StartHost();

        await host.Client.GetAsync(Url);

        await host.Dispatcher.Received(1).Send(
            Arg.Is<GetWaterHeaterHotWaterQuery>(q => !q.IsAdmin && q.Period == HotWaterPeriod.Day && q.Date == null),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("?period=hour")]
    [InlineData("?period=1")]
    [InlineData("?date=2026-02-30")]
    [InlineData("?date=21.11.2026")]
    public async Task ABadPeriodOrDate_IsA400_BeforeTheHandler(string query)
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await host.Client.GetAsync(Url + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await host.Dispatcher.DidNotReceive().Send(
            Arg.Any<GetWaterHeaterHotWaterQuery>(), Arg.Any<CancellationToken>());
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
