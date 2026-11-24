using System.Net;
using System.Net.Http.Json;
using Application.WaterHeaters;
using Domain.Common;
using Features.WaterHeaters.Commands;
using Features.WaterHeaters.Queries;
using NSubstitute;

namespace Features.Tests.Http;

/// <summary>The real pipeline for the Admin water heater endpoints: roles, validation and the error mapping.</summary>
[Collection(FastEndpointsHostCollection.Name)]
public class WaterHeaterAdminHttpTests
{
    private static readonly Guid LocationId = Guid.NewGuid();
    private static readonly string Url = $"/api/admin/locations/{LocationId}/water-heater";
    private const string Key = "TESTKEY-must-not-leak-0000";
    private const string DeviceId = "abcdef0123456789abcdef0123456789";

    private static readonly WaterHeaterSettingsResponse Settings =
        new(LocationId, "abcdef…456789", DateTime.UtcNow, DateTime.UtcNow, new DateOnly(2027, 11, 19));

    private static Task<EndpointHost> StartHost(
        HostCaller caller = HostCaller.Admin, Error? connectError = null, Error? otherError = null) =>
        EndpointHost.StartAsync(dispatcher =>
        {
            dispatcher.Send(Arg.Any<ConnectWaterHeaterCommand>(), Arg.Any<CancellationToken>())
                .Returns(connectError is null
                    ? Result.Success(Settings)
                    : Result.Failure<WaterHeaterSettingsResponse>(connectError));
            dispatcher.Send(Arg.Any<GetWaterHeaterSettingsQuery>(), Arg.Any<CancellationToken>())
                .Returns(otherError is null
                    ? Result.Success(Settings)
                    : Result.Failure<WaterHeaterSettingsResponse>(otherError));
            dispatcher.Send(Arg.Any<SetWaterHeaterKeyExpiryCommand>(), Arg.Any<CancellationToken>())
                .Returns(otherError is null
                    ? Result.Success(Settings with { KeyExpiresOn = new DateOnly(2027, 12, 1) })
                    : Result.Failure<WaterHeaterSettingsResponse>(otherError));
            dispatcher.Send(Arg.Any<RemoveWaterHeaterCommand>(), Arg.Any<CancellationToken>())
                .Returns(otherError is null
                    ? Result.Success(new WaterHeaterRemoved(LocationId))
                    : Result.Failure<WaterHeaterRemoved>(otherError));
        }, caller);

    private static Task<HttpResponseMessage> Put(EndpointHost host, string key = Key, string deviceId = DeviceId) =>
        host.Client.PutAsJsonAsync(Url, new { subscriptionKey = key, deviceId });

    [Fact]
    public async Task Connect_AsAdmin_Is200WithoutTheKey()
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await Put(host);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("deviceIdHint", body);
        Assert.DoesNotContain("subscriptionKey", body);
        Assert.DoesNotContain(Key, body);
        Assert.DoesNotContain(DeviceId, body);
    }

    [Theory]
    [InlineData("", DeviceId)]
    [InlineData(Key, "")]
    [InlineData(Key, "not-hex!")]
    [InlineData("has space", DeviceId)]
    public async Task Connect_WithAnInvalidRequest_Is400BeforeTheHandler(string key, string deviceId)
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await Put(host, key, deviceId);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await host.Dispatcher.DidNotReceive()
            .Send(Arg.Any<ConnectWaterHeaterCommand>(), Arg.Any<CancellationToken>());
    }

    public static TheoryData<string, int> ConnectErrors => new()
    {
        { "WaterHeater.Rejected", 400 },
        { "WaterHeater.DeviceInUse", 409 },
        { "WaterHeater.InvalidResponse", 502 },
        { "WaterHeater.Unavailable", 503 },
        { "WaterHeater.EncryptionNotConfigured", 503 },
        { "Location.NotFound", 404 },
    };

    [Theory]
    [MemberData(nameof(ConnectErrors))]
    public async Task Connect_MapsErrors(string code, int status)
    {
        Error error = code switch
        {
            "WaterHeater.Rejected" => WaterHeaterErrors.Rejected,
            "WaterHeater.DeviceInUse" => WaterHeaterErrors.DeviceInUse,
            "WaterHeater.InvalidResponse" => WaterHeaterErrors.InvalidResponse,
            "WaterHeater.Unavailable" => WaterHeaterErrors.Unavailable,
            "WaterHeater.EncryptionNotConfigured" => WaterHeaterErrors.EncryptionNotConfigured,
            _ => Error.NotFound("Location.NotFound", "Location not found")
        };
        await using EndpointHost host = await StartHost(connectError: error);

        HttpResponseMessage response = await Put(host);

        Assert.Equal(status, (int)response.StatusCode);
    }

    [Fact]
    public async Task GetAndDelete_AsAdmin_Are200And204()
    {
        await using EndpointHost host = await StartHost();

        Assert.Equal(HttpStatusCode.OK, (await host.Client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await host.Client.DeleteAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task GetAndDelete_WithoutAHeater_Are404()
    {
        await using EndpointHost host = await StartHost(otherError: WaterHeaterErrors.NotConnected);

        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.DeleteAsync(Url)).StatusCode);
    }

    [Theory]
    [InlineData(HostCaller.Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(HostCaller.User, HttpStatusCode.Forbidden)]
    public async Task NonAdmins_AreRefused(HostCaller caller, HttpStatusCode expected)
    {
        await using EndpointHost host = await StartHost(caller);

        Assert.Equal(expected, (await Put(host)).StatusCode);
        Assert.Equal(expected, (await host.Client.GetAsync(Url)).StatusCode);
        Assert.Equal(expected, (await host.Client.DeleteAsync(Url)).StatusCode);
    }

    // ---- the OSO key's expiry date (spec 015) ---------------------------------------------------------------------

    [Fact]
    public async Task Connect_WithAMalformedExpiryDate_Is400BeforeTheHandler()
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await host.Client.PutAsJsonAsync(Url,
            new { subscriptionKey = Key, deviceId = DeviceId, keyExpiresOn = "2027-13-01" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("The expiry date must be yyyy-MM-dd", await response.Content.ReadAsStringAsync());
        await host.Dispatcher.DidNotReceive().Send(Arg.Any<ConnectWaterHeaterCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Connect_PassesTheExpiryDateOn_AndAnswersWithIt()
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await host.Client.PutAsJsonAsync(Url,
            new { subscriptionKey = Key, deviceId = DeviceId, keyExpiresOn = "2027-11-19" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"keyExpiresOn\":\"2027-11-19\"", await response.Content.ReadAsStringAsync());
        await host.Dispatcher.Received(1).Send(
            Arg.Is<ConnectWaterHeaterCommand>(c => c.SubscriptionKeyExpiresOn == new DateOnly(2027, 11, 19)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetExpiry_AsAdmin_IsTheSettingsWithTheNewDate()
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response =
            await host.Client.PutAsJsonAsync(Url + "/key-expiry", new { keyExpiresOn = "2027-12-01" });
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"keyExpiresOn\":\"2027-12-01\"", body);
        Assert.DoesNotContain("subscriptionKey", body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("01.12.2027")]
    public async Task SetExpiry_WithAMissingOrMalformedDate_Is400(string date)
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await host.Client.PutAsJsonAsync(Url + "/key-expiry", new { keyExpiresOn = date });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await host.Dispatcher.DidNotReceive()
            .Send(Arg.Any<SetWaterHeaterKeyExpiryCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetExpiry_WithoutAHeater_Is404()
    {
        await using EndpointHost host = await StartHost(otherError: WaterHeaterErrors.NotConnected);

        HttpResponseMessage response =
            await host.Client.PutAsJsonAsync(Url + "/key-expiry", new { keyExpiresOn = "2027-12-01" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(HostCaller.Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(HostCaller.User, HttpStatusCode.Forbidden)]
    public async Task SetExpiry_IsForAdminsOnly(HostCaller caller, HttpStatusCode expected)
    {
        await using EndpointHost host = await StartHost(caller);

        HttpResponseMessage response =
            await host.Client.PutAsJsonAsync(Url + "/key-expiry", new { keyExpiresOn = "2027-12-01" });

        Assert.Equal(expected, response.StatusCode);
    }
}
