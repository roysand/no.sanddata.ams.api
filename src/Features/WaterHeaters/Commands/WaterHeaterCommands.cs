using Application.CQRS;
using Domain.Common;
using Features.WaterHeaters.Queries;

namespace Features.WaterHeaters.Commands;

/// <summary>
/// Connects or replaces the heater of a location. <see cref="ToString"/> never prints the key or device id.
/// <paramref name="SubscriptionKeyExpiresOn"/> is the key's last valid Oslo day; null means one year from today.
/// </summary>
public sealed record ConnectWaterHeaterCommand(
    Guid ActingUserId, Guid LocationId, string SubscriptionKey, string DeviceId, DateOnly? SubscriptionKeyExpiresOn = null)
    : ICommand<Result<WaterHeaterSettingsResponse>>
{
    public override string ToString() => $"ConnectWaterHeaterCommand(LocationId={LocationId})";
}

/// <summary>Changes only the OSO key's expiry date; OSO is not called and the key is not needed (spec 015).</summary>
public sealed record SetWaterHeaterKeyExpiryCommand(Guid ActingUserId, Guid LocationId, DateOnly SubscriptionKeyExpiresOn)
    : ICommand<Result<WaterHeaterSettingsResponse>>;

public sealed record RemoveWaterHeaterCommand(Guid ActingUserId, Guid LocationId) : ICommand<Result<WaterHeaterRemoved>>;

public sealed record WaterHeaterRemoved(Guid LocationId);
