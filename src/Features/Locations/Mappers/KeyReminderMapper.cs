using Application.Common.ApiKeys;
using Features.Locations.Queries;

namespace Features.Locations.Mappers;

public static class KeyReminderMapper
{
    public static KeyRemindersResponse ToResponse(DateOnly today, int windowDays, IReadOnlyList<KeyReminder> due) =>
        new(today, windowDays, due.Select(ToResponse).ToList());

    private static KeyReminderResponse ToResponse(KeyReminder r) =>
        new(r.LocationId, r.LocationName, r.Kind.ToString(), r.ExpiresOn, r.ExpiresAtUtc, r.DaysLeft, r.Expired);
}
