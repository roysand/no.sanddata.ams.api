using System.Net;
using System.Text.Json;
using Application.WaterHeaters;
using Domain.Common;
using Features.WaterHeaters.Commands;
using Features.WaterHeaters.Queries;
using NSubstitute;

namespace Features.Tests.Http;

/// <summary>
/// The real pipeline for the High Demand switches: Admin only, the route, the JSON and the errors (spec 022).
/// </summary>
[Collection(FastEndpointsHostCollection.Name)]
public class WaterHeaterHighDemandHttpTests
{
    private static readonly Guid LocationId = Guid.NewGuid();
    private static readonly string Url = $"/api/admin/locations/{LocationId}/water-heater/high-demand";

    private static readonly WaterHeaterStateResponse State = new(
        LocationId,
        new DateTime(2026, 11, 26, 5, 21, 20, DateTimeKind.Utc),
        AgeSeconds: 0,
        IsStale: false,
        IsOnline: true,
        IsHeating: true,
        SetPointCelsius: 72m,
        new WaterHeaterTemperatures(70m, 69m, 60m, null),
        new WaterHeaterHotWater(339, 360, 3, 1, 13.9m),
        PowerKw: 2.0m,
        new WaterHeaterSpecialMode(
            "Supercharge",
            new DateTime(2026, 11, 26, 5, 21, 13, DateTimeKind.Utc),
            new DateTime(2026, 11, 27, 5, 21, 13, DateTimeKind.Utc),
            "SuperchargeMode"),
        new WaterHeaterOsoCodes(0, 1, 2, 3));

    private static Task<EndpointHost> StartHost(
        HostCaller caller = HostCaller.Admin, Error? error = null, bool withState = true) =>
        EndpointHost.StartAsync(dispatcher =>
            dispatcher.Send(Arg.Any<SetWaterHeaterHighDemandCommand>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    SetWaterHeaterHighDemandCommand command = call.Arg<SetWaterHeaterHighDemandCommand>();
                    return error is null
                        ? Result.Success(new WaterHeaterHighDemandResponse(
                            command.LocationId, command.On, withState ? State : null))
                        : Result.Failure<WaterHeaterHighDemandResponse>(error);
                }), caller);

    private static Task<HttpResponseMessage> Switch(EndpointHost host, bool on, string url = "") =>
        on
            ? host.Client.PutAsync(url == "" ? Url : url, null)
            : host.Client.DeleteAsync(url == "" ? Url : url);

    [Fact]
    public async Task On_AsAdmin_Is200WithTheStateReadBack()
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await Switch(host, on: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement root = json.RootElement;
        Assert.True(root.GetProperty("highDemand").GetBoolean());
        JsonElement mode = root.GetProperty("state").GetProperty("specialMode");
        Assert.Equal("Supercharge", mode.GetProperty("mode").GetString());
        Assert.StartsWith("2026-11-26T05:21:13", mode.GetProperty("start").GetString());
        Assert.StartsWith("2026-11-27T05:21:13", mode.GetProperty("end").GetString());
        await host.Dispatcher.Received(1).Send(
            Arg.Is<SetWaterHeaterHighDemandCommand>(c => c.On && c.LocationId == LocationId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Off_AsAdmin_Is200AndSendsOff()
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await Switch(host, on: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("highDemand").GetBoolean());
        await host.Dispatcher.Received(1).Send(
            Arg.Is<SetWaterHeaterHighDemandCommand>(c => !c.On && c.LocationId == LocationId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedReadBack_IsWrittenAsANullState()
    {
        await using EndpointHost host = await StartHost(withState: false);

        HttpResponseMessage response = await Switch(host, on: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("state").ValueKind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ALocationIdThatIsNotAGuid_Is400AndSendsNothing(bool on)
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response =
            await Switch(host, on, "/api/admin/locations/not-a-guid/water-heater/high-demand");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertNothingSent(host);
    }

    public static TheoryData<string, HttpStatusCode> Errors => new()
    {
        { "Location.NotFound", HttpStatusCode.NotFound },
        { "WaterHeater.NotConnected", HttpStatusCode.NotFound },
        { "WaterHeater.Rejected", HttpStatusCode.BadGateway },
        { "WaterHeater.HighDemandNotOffered", HttpStatusCode.BadGateway },
        { "WaterHeater.InvalidResponse", HttpStatusCode.BadGateway },
        { "WaterHeater.KeyUnreadable", HttpStatusCode.BadGateway },
        { "WaterHeater.Unavailable", HttpStatusCode.ServiceUnavailable },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task Errors_AreMapped(string code, HttpStatusCode expected)
    {
        Error error = code switch
        {
            "Location.NotFound" => Error.NotFound("Location.NotFound", "Location not found"),
            "WaterHeater.NotConnected" => WaterHeaterErrors.NotConnected,
            "WaterHeater.Rejected" => WaterHeaterErrors.Rejected,
            "WaterHeater.HighDemandNotOffered" => WaterHeaterErrors.HighDemandNotOffered,
            "WaterHeater.InvalidResponse" => WaterHeaterErrors.InvalidResponse,
            "WaterHeater.KeyUnreadable" => WaterHeaterErrors.KeyUnreadable,
            _ => WaterHeaterErrors.Unavailable
        };
        await using EndpointHost host = await StartHost(error: error);

        HttpResponseMessage on = await Switch(host, on: true);
        HttpResponseMessage off = await Switch(host, on: false);

        Assert.Equal(expected, on.StatusCode);
        Assert.Equal(expected, off.StatusCode);
        Assert.Contains(error.Description, await on.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(HostCaller.Anonymous, true, HttpStatusCode.Unauthorized)]
    [InlineData(HostCaller.Anonymous, false, HttpStatusCode.Unauthorized)]
    [InlineData(HostCaller.User, true, HttpStatusCode.Forbidden)]
    [InlineData(HostCaller.User, false, HttpStatusCode.Forbidden)]
    public async Task NotAnAdmin_IsRefusedAndNothingIsSent(HostCaller caller, bool on, HttpStatusCode expected)
    {
        await using EndpointHost host = await StartHost(caller);

        HttpResponseMessage response = await Switch(host, on);

        Assert.Equal(expected, response.StatusCode);
        await AssertNothingSent(host);
    }

    private static async Task AssertNothingSent(EndpointHost host) =>
        await host.Dispatcher.DidNotReceive().Send(
            Arg.Any<SetWaterHeaterHighDemandCommand>(), Arg.Any<CancellationToken>());
}
