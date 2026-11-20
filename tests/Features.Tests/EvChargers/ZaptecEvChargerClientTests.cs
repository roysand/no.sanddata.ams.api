using System.Net;
using Application.EvChargers;
using Domain.Common;
using Infrastructure.External;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Features.Tests.EvChargers;

public class ZaptecEvChargerClientTests
{
    private static readonly DateTime From = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc);

    private static ZaptecEvChargerClient Client(
        ZaptecStubHandler handler, bool configured = true, ZaptecTokenCache? cache = null, TimeSpan? timeout = null) =>
        new(
            handler.CreateClient(timeout),
            Options.Create(configured
                ? new ZaptecOptions { Username = ZaptecSamples.Username, Password = ZaptecSamples.Password }
                : new ZaptecOptions()),
            cache ?? new ZaptecTokenCache(TimeProvider.System),
            NullLogger<ZaptecEvChargerClient>.Instance);

    [Fact]
    public async Task GetChargersAsync_ReadsThePascalCaseAnswer()
    {
        Result<IReadOnlyList<ZaptecCharger>> result = await Client(new ZaptecStubHandler()).GetChargersAsync(default);

        Assert.True(result.IsSuccess);
        ZaptecCharger charger = Assert.Single(result.Value);
        Assert.Equal(new ZaptecCharger(
            ZaptecSamples.ChargerId, "Test charger", ZaptecSamples.InstallationId, "Test installation"), charger);
    }

    [Fact]
    public async Task SignsInWithThePasswordGrantAndAUserAgent()
    {
        var handler = new ZaptecStubHandler();

        await Client(handler).GetChargersAsync(default);

        ZaptecStubHandler.Recorded token = handler.Requests.Single(r => r.Path == ZaptecStubHandler.TokenPath);
        Assert.Contains("grant_type=password", token.Body);
        Assert.Contains("scope=openid", token.Body);
        Assert.Contains("username=" + Uri.EscapeDataString(ZaptecSamples.Username), token.Body);
        Assert.Equal("AmsApi/1.0", token.UserAgent);
        ZaptecStubHandler.Recorded data = handler.Requests.Single(r => r.Path == ZaptecStubHandler.ChargersPath);
        Assert.Equal("Bearer " + ZaptecSamples.Token, data.Authorization);
        Assert.Equal("AmsApi/1.0", data.UserAgent);
    }

    [Fact]
    public async Task ReusesTheTokenForLaterCalls()
    {
        var handler = new ZaptecStubHandler();
        ZaptecEvChargerClient client = Client(handler);

        await client.GetChargersAsync(default);
        await client.GetCompletedSessionsAsync(ZaptecSamples.ChargerId, From, To, default);

        Assert.Equal(1, handler.Count(ZaptecStubHandler.TokenPath));
    }

    [Fact]
    public async Task GetCompletedSessionsAsync_FollowsTheCursorAndMapsTheSessions()
    {
        var handler = new ZaptecStubHandler();

        Result<IReadOnlyList<ZaptecSession>> result =
            await Client(handler).GetCompletedSessionsAsync(ZaptecSamples.ChargerId, From, To, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Count(ZaptecStubHandler.SessionsPath));
        string firstQuery = handler.Requests.First(r => r.Path == ZaptecStubHandler.SessionsPath).Query;
        Assert.Contains($"ChargerId={ZaptecSamples.ChargerId}", firstQuery);
        Assert.Contains("From=2026-10-01T00%3A00%3A00Z", firstQuery);
        Assert.Contains("To=2026-11-21T00%3A00%3A00Z", firstQuery);
        Assert.Contains("cursor=page-2", handler.Requests.Last(r => r.Path == ZaptecStubHandler.SessionsPath).Query);

        // The session without an end is skipped.
        Assert.Equal(
            [ZaptecSamples.FixtureSessionId, ZaptecSamples.VoidedSessionId, ZaptecSamples.ReplacedSessionId,
                ZaptecSamples.NoDetailsSessionId],
            result.Value.Select(s => s.Id));

        ZaptecSession real = result.Value[0];
        Assert.Equal(new DateTime(2026, 10, 4, 17, 52, 8, 130, DateTimeKind.Utc), real.StartUtc);
        Assert.Equal(DateTimeKind.Utc, real.StartUtc.Kind);
        Assert.Equal(new DateTime(2026, 10, 5, 11, 28, 38, 87, DateTimeKind.Utc), real.EndUtc);
        Assert.Equal(33.982m, real.Kwh);
        Assert.Equal(12, real.Details.Count);
        Assert.Equal(33.982m, Math.Round(real.Details.Sum(p => p.Kwh), 6));
        Assert.False(real.Discard);

        Assert.True(result.Value[1].Discard);  // voided
        Assert.True(result.Value[2].Discard);  // replaced
        Assert.Empty(result.Value[3].Details); // energyDetails null
    }

    [Fact]
    public async Task NotConfigured_SendsNothing()
    {
        var handler = new ZaptecStubHandler();
        ZaptecEvChargerClient client = Client(handler, configured: false);

        Assert.False(client.IsConfigured);
        Assert.Equal(EvChargerErrors.NotConfigured, (await client.GetChargersAsync(default)).Error);
        Assert.Equal(EvChargerErrors.NotConfigured,
            (await client.GetCompletedSessionsAsync(ZaptecSamples.ChargerId, From, To, default)).Error);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task ARejectedSignIn_IsSignInRejected(HttpStatusCode status)
    {
        ZaptecStubHandler handler = new ZaptecStubHandler().Answer(ZaptecStubHandler.TokenPath, status, "{\"error\":\"invalid_grant\"}");

        Result<IReadOnlyList<ZaptecCharger>> result = await Client(handler).GetChargersAsync(default);

        Assert.Equal(EvChargerErrors.SignInRejected, result.Error);
        Assert.Equal(0, handler.Count(ZaptecStubHandler.ChargersPath));
    }

    [Fact]
    public async Task A401OnData_SignsInOnceMoreAndRetries()
    {
        ZaptecStubHandler handler = new ZaptecStubHandler().Answer(
            ZaptecStubHandler.ChargersPath,
            (HttpStatusCode.Unauthorized, "{}"),
            (HttpStatusCode.OK, ZaptecSamples.Chargers));

        Result<IReadOnlyList<ZaptecCharger>> result = await Client(handler).GetChargersAsync(default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Count(ZaptecStubHandler.TokenPath));
        Assert.Equal(2, handler.Count(ZaptecStubHandler.ChargersPath));
    }

    [Fact]
    public async Task A401TwiceOnData_IsSignInRejected()
    {
        ZaptecStubHandler handler = new ZaptecStubHandler()
            .Answer(ZaptecStubHandler.ChargersPath, HttpStatusCode.Unauthorized);

        Result<IReadOnlyList<ZaptecCharger>> result = await Client(handler).GetChargersAsync(default);

        Assert.Equal(EvChargerErrors.SignInRejected, result.Error);
        Assert.Equal(2, handler.Count(ZaptecStubHandler.ChargersPath));
    }

    [Fact]
    public async Task A429_WaitsAndRetriesOnce()
    {
        ZaptecStubHandler handler = new ZaptecStubHandler().Answer(
            ZaptecStubHandler.ChargersPath,
            (HttpStatusCode.TooManyRequests, "{}"),
            (HttpStatusCode.OK, ZaptecSamples.Chargers));

        Result<IReadOnlyList<ZaptecCharger>> result = await Client(handler).GetChargersAsync(default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, handler.Count(ZaptecStubHandler.ChargersPath));
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A5xx_IsUnavailable(HttpStatusCode status)
    {
        ZaptecStubHandler handler = new ZaptecStubHandler().Answer(ZaptecStubHandler.SessionsPath, status);

        Result<IReadOnlyList<ZaptecSession>> result =
            await Client(handler).GetCompletedSessionsAsync(ZaptecSamples.ChargerId, From, To, default);

        Assert.Equal(EvChargerErrors.Unavailable, result.Error);
    }

    [Fact]
    public async Task ATimeout_IsUnavailable()
    {
        var handler = new ZaptecStubHandler { Delay = TimeSpan.FromSeconds(5) };

        Result<IReadOnlyList<ZaptecCharger>> result =
            await Client(handler, timeout: TimeSpan.FromMilliseconds(50)).GetChargersAsync(default);

        Assert.Equal(EvChargerErrors.Unavailable, result.Error);
    }

    [Fact]
    public async Task ANetworkError_IsUnavailable()
    {
        var handler = new ZaptecStubHandler { Throw = new HttpRequestException("no route") };

        Result<IReadOnlyList<ZaptecCharger>> result = await Client(handler).GetChargersAsync(default);

        Assert.Equal(EvChargerErrors.Unavailable, result.Error);
    }

    [Fact]
    public async Task BadJson_IsInvalidResponse()
    {
        ZaptecStubHandler handler = new ZaptecStubHandler()
            .Answer(ZaptecStubHandler.SessionsPath, HttpStatusCode.OK, "{not json");

        Result<IReadOnlyList<ZaptecSession>> result =
            await Client(handler).GetCompletedSessionsAsync(ZaptecSamples.ChargerId, From, To, default);

        Assert.Equal(EvChargerErrors.InvalidResponse, result.Error);
    }

    [Fact]
    public async Task TheTokenCache_RenewsFiveMinutesBeforeExpiry()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
        var handler = new ZaptecStubHandler();
        ZaptecEvChargerClient client = Client(handler, cache: new ZaptecTokenCache(time));

        await client.GetChargersAsync(default);
        time.Advance(TimeSpan.FromSeconds(86400) - TimeSpan.FromMinutes(6));
        await client.GetChargersAsync(default);
        Assert.Equal(1, handler.Count(ZaptecStubHandler.TokenPath));

        time.Advance(TimeSpan.FromMinutes(2));
        await client.GetChargersAsync(default);
        Assert.Equal(2, handler.Count(ZaptecStubHandler.TokenPath));
    }

    private sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
