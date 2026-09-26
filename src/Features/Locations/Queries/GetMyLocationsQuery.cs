using Application.CQRS;
using Domain.Common;
using Features.Meters.Commands;

namespace Features.Locations.Queries;

public record GetMyLocationsQuery(Guid UserId) : IQuery<Result<IReadOnlyList<LocationSummaryResponse>>>;

public record LocationSummaryResponse(
    Guid Id,
    string Name,
    string Address,
    string Zone,
    IReadOnlyList<MeterResponse> Meters);
