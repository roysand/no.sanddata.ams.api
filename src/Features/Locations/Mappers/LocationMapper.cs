using Domain.Common.Entities;
using Features.Locations.Commands;
using Features.Locations.Endpoints;
using Features.Locations.Queries;
using Features.Meters.Mappers;

namespace Features.Locations.Mappers;

public static class LocationMapper
{
    public const string StatusActive = "Active";
    public const string StatusExpired = "Expired";
    public const string StatusDeactivated = "Deactivated";

    public static LocationSummaryResponse ToResponse(Location location) =>
        new(location.Id, location.Name, location.Address, location.Zone,
            location.Meters.Select(MeterMapper.ToResponse).ToList());

    public static UpdateLocationCommand ToCommand(Guid actingUserId, UpdateLocationRequest request) =>
        new(actingUserId, request.Id, request.Name.Trim(), request.Address.Trim(), request.SerialNumber.Trim(), request.Zone,
            request.HasNorgesPriceAgreement, request.IsActive);

    public static CreateLocationCommand ToCommand(Guid actingUserId, CreateLocationRequest request) =>
        new(actingUserId, request.Name.Trim(), request.Address.Trim(), request.SerialNumber.Trim(), request.Zone,
            request.HasNorgesPriceAgreement, request.IsActive);

    /// <summary>A user creating their own location: the same command, plus the link to the caller.</summary>
    public static CreateLocationCommand ToOwnCommand(Guid userId, CreateLocationRequest request) =>
        ToCommand(userId, request) with { LinkToUserId = userId };

    public static AdminLocationResponse ToAdminResponse(Location location, DateTime now) =>
        new(location.Id, location.Name, location.Address, location.SerialNumber, location.Zone, location.IsActive,
            location.HasNorgesPriceAgreement, ToKeyInfo(location.ApiKey, now),
            location.Meters.Select(MeterMapper.ToResponse).ToList());

    public static ApiKeyInfoResponse ToKeyInfo(ApiKey apiKey, DateTime now) =>
        new(apiKey.Description, apiKey.KeyHint, apiKey.IsActive, apiKey.ExpiresAt, KeyStatus(apiKey, now));

    /// <summary>Deactivated wins over Expired: an admin switched it off on purpose.</summary>
    public static string KeyStatus(ApiKey apiKey, DateTime now) =>
        !apiKey.IsActive ? StatusDeactivated : apiKey.ExpiresAt <= now ? StatusExpired : StatusActive;
}
