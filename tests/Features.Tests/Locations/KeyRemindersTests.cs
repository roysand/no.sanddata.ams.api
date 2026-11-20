using Application.Common.ApiKeys;

namespace Features.Tests.Locations;

public class KeyRemindersTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly Guid Home = Guid.NewGuid();
    private static readonly Guid Cabin = Guid.NewGuid();

    private static HeaterKeyFacts Heater(DateOnly expiresOn, string name = "Hjemme", Guid? id = null) =>
        new(id ?? Home, name, expiresOn);

    private static SensorKeyFacts Sensor(
        DateTime expiresAtUtc, string name = "Hytta", bool locationActive = true, bool keyActive = true, Guid? id = null) =>
        new(id ?? Cabin, name, locationActive, keyActive, expiresAtUtc);

    private static IReadOnlyList<KeyReminder> Due(params HeaterKeyFacts[] heaters) => KeyReminders.Due(Today, [], heaters);

    [Theory]
    [InlineData(31, false, false)]
    [InlineData(30, true, false)]
    [InlineData(0, true, false)]
    [InlineData(-1, true, true)]
    [InlineData(-400, true, true)]
    public void TheWindow_IsThirtyDays_AndExpiredKeysStayListed(int daysLeft, bool listed, bool expired)
    {
        IReadOnlyList<KeyReminder> due = Due(Heater(Today.AddDays(daysLeft)));

        Assert.Equal(listed, due.Count == 1);
        if (listed)
        {
            Assert.Equal(daysLeft, due[0].DaysLeft);
            Assert.Equal(expired, due[0].Expired);
            Assert.Equal(KeyKind.WaterHeaterKey, due[0].Kind);
            Assert.Null(due[0].ExpiresAtUtc);
        }
    }

    [Fact]
    public void ASensorKeyExpiringDuringTheDay_IsValidThroughThatDay()
    {
        var expiresAt = new DateTime(2026, 12, 2, 10, 0, 0, DateTimeKind.Utc);

        KeyReminder r = Assert.Single(KeyReminders.Due(Today, [Sensor(expiresAt)], []));

        Assert.Equal(new DateOnly(2026, 12, 2), r.ExpiresOn);
        Assert.Equal(12, r.DaysLeft);
        Assert.Equal(expiresAt, r.ExpiresAtUtc);
        Assert.Equal(KeyKind.SensorKey, r.Kind);
    }

    [Fact]
    public void ASensorKeyExpiringAtOsloMidnight_WasLastValidTheDayBefore()
    {
        // 2028-11-19T23:00Z is the start of 20 November in Oslo (winter time, UTC+1).
        Assert.Equal(new DateOnly(2028, 11, 19),
            KeyReminders.LastValidDay(new DateTime(2028, 11, 19, 23, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2028, 11, 20),
            KeyReminders.LastValidDay(new DateTime(2028, 11, 20, 10, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void DaysLeft_AreWholeDays_AcrossTheEndOfSummerTime()
    {
        // Summer time ends on 25 October 2026; 30 Oslo days from 10 October is 9 November, not a day less.
        IReadOnlyList<KeyReminder> due = KeyReminders.Due(new DateOnly(2026, 10, 10), [],
            [Heater(new DateOnly(2026, 11, 9))]);

        Assert.Equal(30, Assert.Single(due).DaysLeft);
    }

    [Fact]
    public void DeactivatedKeys_AndInactiveLocations_AreLeftOut()
    {
        DateTime soon = new(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc);

        IReadOnlyList<KeyReminder> due = KeyReminders.Due(Today,
            [Sensor(soon, keyActive: false), Sensor(soon, locationActive: false)], []);

        Assert.Empty(due);
    }

    [Fact]
    public void TheList_IsSoonestFirst_ThenByName_ThenSensorBeforeHeater()
    {
        Guid a = Guid.NewGuid();
        IReadOnlyList<KeyReminder> due = KeyReminders.Due(Today,
            [Sensor(new DateTime(2026, 11, 25, 12, 0, 0, DateTimeKind.Utc), name: "Hytta", id: a)],
            [
                Heater(new DateOnly(2026, 11, 25), name: "Hytta", id: a),
                Heater(new DateOnly(2026, 11, 25), name: "Anneks"),
                Heater(new DateOnly(2026, 11, 22), name: "Hjemme"),
            ]);

        Assert.Equal(
            [("Hjemme", KeyKind.WaterHeaterKey), ("Anneks", KeyKind.WaterHeaterKey), ("Hytta", KeyKind.SensorKey),
                ("Hytta", KeyKind.WaterHeaterKey)],
            due.Select(r => (r.LocationName, r.Kind)));
    }
}
