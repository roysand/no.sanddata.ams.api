using Application.CQRS;
using Domain.Common;
using Features.Locations.Queries;

namespace Features.Locations.Commands;

public record CreateLocationCommand(
    Guid ActingUserId,
    string Name,
    string Address,
    string SerialNumber,
    string Zone,
    bool HasNorgesPriceAgreement,
    bool IsActive) : ICommand<Result<CreatedLocationResponse>>;

/// <summary>The only place a full sensor key is ever returned (together with the rotate response).</summary>
public record CreatedLocationResponse(AdminLocationResponse Location, string ApiKey);
