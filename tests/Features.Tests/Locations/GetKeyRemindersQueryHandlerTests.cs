using Application.Common.ApiKeys;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Locations.Handlers;
using Features.Locations.Queries;
using Features.Tests.NorgesPris;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Features.Tests.Locations;

public class GetKeyRemindersQueryHandlerTests
{
    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IWaterHeaterRepository<WaterHeater> _heaters = Substitute.For<IWaterHeaterRepository<WaterHeater>>();

    private Task<Result<KeyRemindersResponse>> List() =>
        new GetKeyRemindersQueryHandler(_locations, _heaters, new FixedTimeProvider("2026-11-20T10:00:00Z"),
                NullLogger<GetKeyRemindersQueryHandler>.Instance)
            .Handle(new GetKeyRemindersQuery(Guid.NewGuid()), default);

    private static Location WithKey(string name, DateTime expiresAt, bool active = true)
    {
        var location = new Location(Guid.NewGuid(), name, "Addr", "SN-" + name, "NO1", active, false);
        location.AssignApiKey(new ApiKey(Guid.NewGuid(), ApiKeyCrypto.Hash("k-" + name), "k", "Sensor key for " + name,
            true, expiresAt));
        return location;
    }

    [Fact]
    public async Task ListsBothKindsWithTheLocationNames_CountedFromTodayInOslo()
    {
        Location cabin = WithKey("Hytta", new DateTime(2026, 12, 2, 9, 14, 3, DateTimeKind.Utc));
        Location home = WithKey("Hjemme", new DateTime(2028, 11, 19, 12, 0, 0, DateTimeKind.Utc));
        _locations.GetAllWithKeyAsync(Arg.Any<CancellationToken>()).Returns([home, cabin]);
        _heaters.GetForActiveLocationsAsync(Arg.Any<CancellationToken>()).Returns(
            [new WaterHeater(Guid.NewGuid(), home.Id, "abc123", "v1:protected", new DateOnly(2026, 11, 17))]);

        KeyRemindersResponse result = (await List()).Value;

        Assert.Equal(new DateOnly(2026, 11, 20), result.Today);
        Assert.Equal(30, result.WindowDays);
        Assert.Collection(result.Items,
            first =>
            {
                Assert.Equal(("Hjemme", "WaterHeaterKey", -3, true), (first.LocationName, first.Kind, first.DaysLeft,
                    first.Expired));
                Assert.Equal(home.Id, first.LocationId);
                Assert.Null(first.ExpiresAt);
            },
            second =>
            {
                Assert.Equal(("Hytta", "SensorKey", 12, false), (second.LocationName, second.Kind, second.DaysLeft,
                    second.Expired));
                Assert.Equal(new DateOnly(2026, 12, 2), second.ExpiresOn);
                Assert.Equal(new DateTime(2026, 12, 2, 9, 14, 3, DateTimeKind.Utc), second.ExpiresAt);
            });
    }

    [Fact]
    public async Task NothingDue_GivesAnEmptyList_AfterOneReadOfEach()
    {
        _locations.GetAllWithKeyAsync(Arg.Any<CancellationToken>())
            .Returns([WithKey("Hjemme", new DateTime(2028, 11, 19, 12, 0, 0, DateTimeKind.Utc))]);
        _heaters.GetForActiveLocationsAsync(Arg.Any<CancellationToken>()).Returns([]);

        Result<KeyRemindersResponse> result = await List();

        Assert.Empty(result.Value.Items);
        await _locations.Received(1).GetAllWithKeyAsync(Arg.Any<CancellationToken>());
        await _heaters.Received(1).GetForActiveLocationsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Response_HasNoPropertyThatCouldCarryAKeyItsHashOrADeviceId()
    {
        string[] names = typeof(KeyReminderResponse).GetProperties().Select(p => p.Name)
            .Concat(typeof(KeyRemindersResponse).GetProperties().Select(p => p.Name)).ToArray();

        Assert.DoesNotContain(names, n => n.Contains("Key", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Hash", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Contains("Device", StringComparison.OrdinalIgnoreCase));
    }
}
