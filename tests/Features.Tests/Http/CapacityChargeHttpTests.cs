using System.Net;
using Domain.Common;
using Features.ElectricityCost.Queries;
using NSubstitute;

namespace Features.Tests.Http;

/// <summary>The real pipeline for the capacity-charge endpoint: sign-in, the month parameter, the not-found mapping.</summary>
public class CapacityChargeHttpTests
{
    private static readonly Guid LocationId = Guid.NewGuid();

    private static readonly CapacityChargeResponse Charge = new(
        "2026-10", false, true, [new CapacityPeakResponse(new DateOnly(2026, 10, 14), DateTime.UtcNow, 6.2m)],
        5.4m, new CapacityStepRangeResponse(5m, 10m), 336m, 84m, 420m, 4.6m);

    private static Task<EndpointHost> StartHost(HostCaller caller = HostCaller.User, bool found = true) =>
        EndpointHost.StartAsync(
            dispatcher => dispatcher.Send(Arg.Any<GetCapacityChargeQuery>(), Arg.Any<CancellationToken>())
                .Returns(found
                    ? Result.Success(Charge)
                    : Result.Failure<CapacityChargeResponse>(Error.NotFound("Location.NotFound", "Location not found"))),
            caller);

    [Theory]
    [InlineData("&month=2026-10")]
    [InlineData("")]
    public async Task ASignedInUser_GetsTheCharge(string month)
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response =
            await host.Client.GetAsync($"/api/electricity-cost/capacity?locationId={LocationId}{month}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("2026-1")]
    [InlineData("october")]
    public async Task AMalformedMonth_IsA400(string month)
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response =
            await host.Client.GetAsync($"/api/electricity-cost/capacity?locationId={LocationId}&month={month}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NotSignedIn_IsA401()
    {
        await using EndpointHost host = await StartHost(HostCaller.Anonymous);

        HttpResponseMessage response = await host.Client.GetAsync($"/api/electricity-cost/capacity?locationId={LocationId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ALocationTheUserIsNotLinkedTo_IsA404()
    {
        await using EndpointHost host = await StartHost(found: false);

        HttpResponseMessage response = await host.Client.GetAsync($"/api/electricity-cost/capacity?locationId={LocationId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
