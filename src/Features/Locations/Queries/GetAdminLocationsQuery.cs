using Application.CQRS;
using Domain.Common;
using Features.Meters.Commands;

namespace Features.Locations.Queries;

public record GetAdminLocationsQuery(Guid ActingUserId) : IQuery<Result<IReadOnlyList<AdminLocationResponse>>>;

/// <summary>Non-secret facts about a location's sensor key. The key and its fingerprint are never exposed.</summary>
public record ApiKeyInfoResponse(string Description, string Hint, bool IsActive, DateTime ExpiresAt, string Status);

public record AdminLocationResponse(
    Guid Id,
    string Name,
    string Address,
    string SerialNumber,
    string Zone,
    bool IsActive,
    bool HasNorgesPriceAgreement,
    ApiKeyInfoResponse ApiKey,
    IReadOnlyList<MeterResponse> Meters);
