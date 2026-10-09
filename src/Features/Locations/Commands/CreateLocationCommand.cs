using Application.CQRS;
using Domain.Common;
using Features.Locations.Queries;

namespace Features.Locations.Commands;

/// <param name="LinkToUserId">When set, the new location is linked to this user in the same save (a user creating their own location).</param>
public record CreateLocationCommand(
    Guid ActingUserId,
    string Name,
    string Address,
    string SerialNumber,
    string Zone,
    bool HasNorgesPriceAgreement,
    bool IsActive,
    Guid? LinkToUserId = null) : ICommand<Result<CreatedLocationResponse>>;

/// <summary>The only place a full sensor key is ever returned (together with the rotate response).</summary>
public record CreatedLocationResponse(AdminLocationResponse Location, string ApiKey);
