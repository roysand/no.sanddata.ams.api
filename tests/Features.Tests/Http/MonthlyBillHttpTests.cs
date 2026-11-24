using System.Net;
using Domain.Common;
using Features.ElectricityCost.Queries;
using NSubstitute;

namespace Features.Tests.Http;

/// <summary>The real pipeline for the bill endpoint: sign-in, the month parameter, the error mapping.</summary>
[Collection(FastEndpointsHostCollection.Name)]
public class MonthlyBillHttpTests
{
    private static readonly Guid LocationId = Guid.NewGuid();

    private static readonly BillPartResponse EmptyPart = new("Strom", [], 0m, 0m, 0m, 0m, 0m);

    private static readonly MonthlyBillResponse Bill = new(
        "2026-10", "Final", "NorgesPris", true, new BillHoursResponse(745, 745, 745, 745), null,
        new BillCapacityResponse(false, 6m, new CapacityStepRangeResponse(5m, 10m)), EmptyPart,
        EmptyPart with { Name = "Nettleie" }, new BillTotalResponse(0m, 0m, 0m, 0m, 0m), null, 0m, null);

    private static Task<EndpointHost> StartHost(
        HostCaller caller = HostCaller.User, Error? error = null, MonthlyBillResponse? bill = null) =>
        EndpointHost.StartAsync(
            dispatcher => dispatcher.Send(Arg.Any<GetMonthlyBillQuery>(), Arg.Any<CancellationToken>())
                .Returns(error is null ? Result.Success(bill ?? Bill) : Result.Failure<MonthlyBillResponse>(error)),
            caller);

    [Fact]
    public async Task TheWaterHeaterPart_IsSerialisedInCamelCase_AndNullWhenAbsent()
    {
        var part = new WaterHeaterBillResponse(
            104.6m, 94.12m, 15180, false,
            new DateTime(2026, 11, 19, 18, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 30, 22, 0, 0, DateTimeKind.Utc),
            Estimate: false);
        await using EndpointHost withPart = await StartHost(bill: Bill with { WaterHeater = part });
        await using EndpointHost without = await StartHost();

        string json = await withPart.Client.GetStringAsync($"/api/electricity-cost/bill?locationId={LocationId}");
        string none = await without.Client.GetStringAsync($"/api/electricity-cost/bill?locationId={LocationId}");

        Assert.Contains("\"waterHeater\":{\"kwh\":104.6,\"cost\":94.12,\"sampledMinutes\":15180,\"capped\":false,", json);
        Assert.Contains("\"firstHourUtc\":\"2026-11-19T18:00:00Z\"", json);
        Assert.Contains("\"estimate\":false", json);
        Assert.Contains("\"waterHeater\":null", none);
    }

    [Fact]
    public async Task TheCarPart_HasItsCostAndTheRule_InCamelCase()
    {
        var part = new EvChargerBillResponse(
            212.7m, false, false, 0,
            new DateTime(2026, 11, 1, 6, 0, 0, DateTimeKind.Utc), new DateTime(2026, 11, 20, 21, 0, 0, DateTimeKind.Utc),
            106.35m, "NorgesPrisPrice");
        await using EndpointHost host = await StartHost(bill: Bill with { EvCharger = part });

        string json = await host.Client.GetStringAsync($"/api/electricity-cost/bill?locationId={LocationId}");

        Assert.Contains("\"lastHour\":\"2026-11-20T21:00:00Z\",\"cost\":106.35,\"pricedAt\":\"NorgesPrisPrice\"}", json);
    }

    [Theory]
    [InlineData("&month=2026-10")]
    [InlineData("")]
    public async Task ASignedInUser_GetsTheBill(string month)
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response =
            await host.Client.GetAsync($"/api/electricity-cost/bill?locationId={LocationId}{month}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("07-2026")]
    [InlineData("2026-1")]
    public async Task AMalformedMonth_IsA400_BeforeAnyHandlerRuns(string month)
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response =
            await host.Client.GetAsync($"/api/electricity-cost/bill?locationId={LocationId}&month={month}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Month must be yyyy-MM", await response.Content.ReadAsStringAsync());
        await host.Dispatcher.DidNotReceive().Send(Arg.Any<GetMonthlyBillQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMonthThatHasNotStarted_IsA400()
    {
        await using EndpointHost host = await StartHost(
            error: Error.Validation("Bill.MonthNotStarted", "The month has not started"));

        HttpResponseMessage response =
            await host.Client.GetAsync($"/api/electricity-cost/bill?locationId={LocationId}&month=2099-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NotSignedIn_IsA401()
    {
        await using EndpointHost host = await StartHost(HostCaller.Anonymous);

        HttpResponseMessage response = await host.Client.GetAsync($"/api/electricity-cost/bill?locationId={LocationId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ALocationTheUserIsNotLinkedTo_IsA404()
    {
        await using EndpointHost host = await StartHost(
            error: Error.NotFound("Location.NotFound", "Location not found"));

        HttpResponseMessage response = await host.Client.GetAsync($"/api/electricity-cost/bill?locationId={LocationId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
