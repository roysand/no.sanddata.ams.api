using Application.CQRS;
using Domain.Common;
using Features.Meters.Commands;

namespace Features.Locations.Queries;

public record GetMyLocationsQuery(Guid UserId) : IQuery<Result<IReadOnlyList<LocationSummaryResponse>>>;

/// <param name="Role">"Owner" or "Viewer"; as text, because the API does not serialise enums by name.</param>
/// <param name="IsActive">Owners also receive their inactive locations so they can switch them on again.</param>
public record LocationSummaryResponse(
    Guid Id,
    string Name,
    string Address,
    string Zone,
    string SerialNumber,
    bool HasNorgesPriceAgreement,
    bool IsActive,
    string Role,
    IReadOnlyList<MeterResponse> Meters);
