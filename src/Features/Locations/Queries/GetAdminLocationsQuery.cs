using Application.CQRS;
using Domain.Common;
using Features.Meters.Commands;

namespace Features.Locations.Queries;

public record GetAdminLocationsQuery(Guid ActingUserId) : IQuery<Result<IReadOnlyList<AdminLocationResponse>>>;

/// <summary>Non-secret facts about a location's sensor key. The key and its fingerprint are never exposed.</summary>
public record ApiKeyInfoResponse(string Description, string Hint, bool IsActive, DateTime ExpiresAt, string Status);

/// <summary>Someone with access to a location. <paramref name="Role"/> is "Owner" or "Viewer".</summary>
public record LocationUserResponse(Guid UserId, string Email, string FirstName, string LastName, string Role);

/// <param name="Users">
/// Who has access. Filled by the admin list and the admin edit; the create response leaves it empty, the
/// caller refetches the list.
/// </param>
public record AdminLocationResponse(
    Guid Id,
    string Name,
    string Address,
    string SerialNumber,
    string Zone,
    bool IsActive,
    bool HasNorgesPriceAgreement,
    ApiKeyInfoResponse ApiKey,
    IReadOnlyList<MeterResponse> Meters,
    IReadOnlyList<LocationUserResponse> Users);
