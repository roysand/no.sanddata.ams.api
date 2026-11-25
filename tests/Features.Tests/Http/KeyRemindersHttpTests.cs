using System.Net;
using Domain.Common;
using Features.Locations.Queries;
using NSubstitute;

namespace Features.Tests.Http;

/// <summary>The real pipeline for the reminder list: sign-in, the Admin role and the JSON shape.</summary>
[Collection(FastEndpointsHostCollection.Name)]
public class KeyRemindersHttpTests
{
    private const string Url = "/api/admin/key-reminders";

    private static readonly KeyRemindersResponse Reminders = new(
        new DateOnly(2026, 11, 20), 30,
        [
            new KeyReminderResponse(Guid.NewGuid(), "Hjemme", "WaterHeaterKey", new DateOnly(2026, 11, 17), null, -3, true),
            new KeyReminderResponse(Guid.NewGuid(), "Hytta", "SensorKey", new DateOnly(2026, 12, 2),
                new DateTime(2026, 12, 2, 9, 14, 3, DateTimeKind.Utc), 12, false),
        ]);

    private static Task<EndpointHost> StartHost(HostCaller caller = HostCaller.Admin) =>
        EndpointHost.StartAsync(
            dispatcher => dispatcher.Send(Arg.Any<GetKeyRemindersQuery>(), Arg.Any<CancellationToken>())
                .Returns(Result.Success(Reminders)),
            caller);

    [Fact]
    public async Task AnAdmin_GetsTheListInTheContractsShape()
    {
        await using EndpointHost host = await StartHost();

        HttpResponseMessage response = await host.Client.GetAsync(Url);
        string body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"today\":\"2026-11-20\"", body);
        Assert.Contains("\"windowDays\":30", body);
        Assert.Contains(
            "\"locationName\":\"Hjemme\",\"kind\":\"WaterHeaterKey\",\"expiresOn\":\"2026-11-17\",\"expiresAt\":null," +
            "\"daysLeft\":-3,\"expired\":true", body);
        Assert.Contains("\"kind\":\"SensorKey\",\"expiresOn\":\"2026-12-02\",\"expiresAt\":\"2026-12-02T09:14:03Z\"", body);
    }

    [Theory]
    [InlineData(HostCaller.Anonymous, HttpStatusCode.Unauthorized)]
    [InlineData(HostCaller.User, HttpStatusCode.Forbidden)]
    public async Task TheList_IsForAdminsOnly(HostCaller caller, HttpStatusCode expected)
    {
        await using EndpointHost host = await StartHost(caller);

        HttpResponseMessage response = await host.Client.GetAsync(Url);

        Assert.Equal(expected, response.StatusCode);
        await host.Dispatcher.DidNotReceive().Send(Arg.Any<GetKeyRemindersQuery>(), Arg.Any<CancellationToken>());
    }
}
