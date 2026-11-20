using Application.CQRS;
using Domain.Common;

namespace Features.Locations.Queries;

public record GetKeyRemindersQuery(Guid ActingUserId) : IQuery<Result<KeyRemindersResponse>>;

/// <param name="Today">The Oslo date the days are counted from.</param>
/// <param name="WindowDays">How many days ahead a key is listed; 30 in this version.</param>
/// <param name="Items">The due keys, soonest first; empty when nothing is due.</param>
public record KeyRemindersResponse(DateOnly Today, int WindowDays, IReadOnlyList<KeyReminderResponse> Items);

/// <param name="Kind">"SensorKey" or "WaterHeaterKey"; as text, because the API does not serialise enums by name.</param>
/// <param name="ExpiresOn">The last day the key is valid.</param>
/// <param name="ExpiresAt">Sensor keys only: the instant the API stops accepting it; null for a water heater key.</param>
/// <param name="DaysLeft">ExpiresOn − Today: 0 is the last day, negative means it has expired.</param>
public record KeyReminderResponse(
    Guid LocationId,
    string LocationName,
    string Kind,
    DateOnly ExpiresOn,
    DateTime? ExpiresAt,
    int DaysLeft,
    bool Expired);
