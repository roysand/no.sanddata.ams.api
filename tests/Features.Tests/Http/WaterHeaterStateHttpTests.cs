using System.Net;
using System.Text.Json;
using Application.WaterHeaters;
using Domain.Common;
using Features.WaterHeaters.Queries;
using NSubstitute;

namespace Features.Tests.Http;

/// <summary>The real pipeline for reading a water heater's state: sign-in, the JSON shape, the error mapping.</summary>
public class WaterHeaterStateHttpTests
{
    private static readonly Guid LocationId = Guid.NewGuid();
    private static readonly string Url = $"/api/locations/{LocationId}/water-heater";

    private static readonly WaterHeaterStateResponse State = new(
        LocationId, new DateTime(2026, 11, 19, 18, 6, 48, DateTimeKind.Utc), 103, false, true, false, 72m,
        new WaterHeaterTemperatures(72.8m, 72.3m, 70.5m, null),
        new WaterHeaterHotWater(344, 360, 3, 1, 13.97741m),
        0.011774m,
        new WaterHeaterSpecialMode("Supercharge", DateTime.UtcNow, DateTime.UtcNow, "SuperchargeMode"),
        new WaterHeaterOsoCodes(0, 1, 2, 3));

    private static Task<EndpointHost> StartHost(HostCaller caller = HostCaller.User, Error? error = null) =>
        EndpointHost.StartAsync(
            dispatcher => dispatcher.Send(Arg.Any<GetWaterHeaterStateQuery>(), Arg.Any<CancellationToken>())
                .Returns(error is null ? Result.Success(State) : Result.Failure<WaterHeaterStateResponse>(error)),
            caller);

    [Fact]
    public async Task ARegularUser_GetsTheStateInTheContractShape()
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await host.Client.GetAsync(Url);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = json.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(103, root.GetProperty("ageSeconds").GetInt32());
        Assert.Equal(72.8m, root.GetProperty("temperatures").GetProperty("topCelsius").GetDecimal());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("temperatures").GetProperty("fourthCelsius").ValueKind);
        Assert.Equal(344, root.GetProperty("hotWater").GetProperty("availableLitres40").GetInt32());
        Assert.Equal("Supercharge", root.GetProperty("specialMode").GetProperty("mode").GetString());
        Assert.Equal(2, root.GetProperty("osoCodes").GetProperty("optimizationSubOption").GetInt32());
        await host.Dispatcher.Received(1).Send(
            Arg.Is<GetWaterHeaterStateQuery>(q => q.LocationId == LocationId && !q.IsAdmin),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAdmin_IsPassedOnAsAdmin()
    {
        await using EndpointHost host = await StartHost(HostCaller.Admin);

        await host.Client.GetAsync(Url);

        await host.Dispatcher.Received(1).Send(
            Arg.Is<GetWaterHeaterStateQuery>(q => q.IsAdmin), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotSignedIn_IsA401()
    {
        await using EndpointHost host = await StartHost(HostCaller.Anonymous);

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client.GetAsync(Url)).StatusCode);
    }

    public static TheoryData<string, int> Errors => new()
    {
        { "Location.NotFound", 404 },
        { "WaterHeater.NotConnected", 404 },
        { "WaterHeater.Rejected", 502 },
        { "WaterHeater.InvalidResponse", 502 },
        { "WaterHeater.KeyUnreadable", 502 },
        { "WaterHeater.Unavailable", 503 },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task Errors_AreMapped(string code, int status)
    {
        Error error = code switch
        {
            "WaterHeater.NotConnected" => WaterHeaterErrors.NotConnected,
            "WaterHeater.Rejected" => WaterHeaterErrors.Rejected,
            "WaterHeater.InvalidResponse" => WaterHeaterErrors.InvalidResponse,
            "WaterHeater.KeyUnreadable" => WaterHeaterErrors.KeyUnreadable,
            "WaterHeater.Unavailable" => WaterHeaterErrors.Unavailable,
            _ => Error.NotFound("Location.NotFound", "Location not found")
        };
        await using EndpointHost host = await StartHost(error: error);

        HttpResponseMessage response = await host.Client.GetAsync(Url);

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Contains(error.Description, await response.Content.ReadAsStringAsync());
    }
}
