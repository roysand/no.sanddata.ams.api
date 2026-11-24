using System.Net;
using System.Text.Json;
using Domain.Common;
using Features.ElectricityCost.Queries;
using NSubstitute;

namespace Features.Tests.Http;

/// <summary>The real pipeline for the Norgespris status endpoint (spec 018): sign-in and the answer's shape.</summary>
[Collection(FastEndpointsHostCollection.Name)]
public class NorgesPrisStatusHttpTests
{
    private const string Url = "/api/electricity-cost/norgespris-status";

    private static readonly NorgesPrisStatusResponse Status = new(
        new DateTime(2026, 11, 22, 14, 37, 12, DateTimeKind.Utc),
        [
            new NorgesPrisStatusItem(Guid.NewGuid(), "2026-11", 1000m, 1043.62m, 1391.8m, true, 43.62m, 0.5m,
                new DateTime(2026, 11, 30, 23, 0, 0, DateTimeKind.Utc))
        ]);

    private static Task<EndpointHost> StartHost(HostCaller caller = HostCaller.User) =>
        EndpointHost.StartAsync(
            dispatcher => dispatcher.Send(Arg.Any<GetNorgesPrisStatusQuery>(), Arg.Any<CancellationToken>())
                .Returns(Result.Success(Status)),
            caller);

    [Fact]
    public async Task ASignedInUser_GetsTheStatus()
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await host.Client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement item = json.RootElement.GetProperty("items")[0];
        Assert.Equal("2026-11", item.GetProperty("month").GetString());
        Assert.Equal(1000m, item.GetProperty("limitKwh").GetDecimal());
        Assert.Equal(1043.62m, item.GetProperty("kwhSoFar").GetDecimal());
        Assert.Equal(1391.8m, item.GetProperty("projectedKwh").GetDecimal());
        Assert.True(item.GetProperty("overLimit").GetBoolean());
        Assert.Equal(43.62m, item.GetProperty("overLimitKwh").GetDecimal());
        Assert.Equal(0.5m, item.GetProperty("norgesPrisPricePerKwh").GetDecimal());
        Assert.True(item.TryGetProperty("resetsAt", out _));
        Assert.True(json.RootElement.TryGetProperty("asOf", out _));
    }

    [Fact]
    public async Task NotSignedIn_IsA401()
    {
        await using EndpointHost host = await StartHost(HostCaller.Anonymous);

        HttpResponseMessage response = await host.Client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
