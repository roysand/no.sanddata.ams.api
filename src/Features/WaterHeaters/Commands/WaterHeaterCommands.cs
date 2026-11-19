using Application.CQRS;
using Domain.Common;
using Features.WaterHeaters.Queries;

namespace Features.WaterHeaters.Commands;

/// <summary>Connects or replaces the heater of a location. <see cref="ToString"/> never prints the key or device id.</summary>
public sealed record ConnectWaterHeaterCommand(Guid ActingUserId, Guid LocationId, string SubscriptionKey, string DeviceId)
    : ICommand<Result<WaterHeaterSettingsResponse>>
{
    public override string ToString() => $"ConnectWaterHeaterCommand(LocationId={LocationId})";
}

public sealed record RemoveWaterHeaterCommand(Guid ActingUserId, Guid LocationId) : ICommand<Result<WaterHeaterRemoved>>;

public sealed record WaterHeaterRemoved(Guid LocationId);
