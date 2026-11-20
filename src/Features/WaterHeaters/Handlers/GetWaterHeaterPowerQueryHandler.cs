using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.WaterHeaters;
using Domain.Common;
using Domain.Common.Entities;
using Features.WaterHeaters.Logging;
using Features.WaterHeaters.Mappers;
using Features.WaterHeaters.Queries;
using Features.WaterHeaters.Services;
using Microsoft.Extensions.Logging;

namespace Features.WaterHeaters.Handlers;

/// <summary>
/// The heater's average power per 5 minutes in a window, from the stored power samples (spec 017). Owners and viewers
/// of an active location may read it, and so may an Admin for any existing location, active or not (research R4);
/// everyone else gets the same 404. It reads only, and needs no connected heater: the samples belong to the location.
/// The validator has already refused a bad window, so this only fills in the defaults and does not check it again
/// (Principle IV).
/// </summary>
public class GetWaterHeaterPowerQueryHandler(
    ILocationRepository<Location> locations,
    IWaterHeaterPowerSampleRepository<WaterHeaterPowerSample> samples,
    TimeProvider clock,
    ILogger<GetWaterHeaterPowerQueryHandler> logger)
    : IQueryHandler<GetWaterHeaterPowerQuery, Result<WaterHeaterPowerResponse>>
{
    private const int StepSeconds = 300;

    private static readonly Error LocationNotFound = Error.NotFound("Location.NotFound", "Location not found");

    public async Task<Result<WaterHeaterPowerResponse>> Handle(GetWaterHeaterPowerQuery query, CancellationToken ct)
    {
        bool mayRead = query.IsAdmin
            ? await locations.GetByIdAsync(query.LocationId, ct) is not null
            : await locations.IsUserAssociatedAsync(query.UserId, query.LocationId, ct);
        if (!mayRead)
        {
            LogMessages.LocationAccessDenied(logger, query.UserId, query.LocationId);
            return Result.Failure<WaterHeaterPowerResponse>(LocationNotFound);
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;
        (DateTime from, DateTime to) = PowerWindow.Resolve(query.From, query.To, now);

        IReadOnlyList<WaterHeaterPowerStep> steps =
            await samples.GetPowerStepsAsync(query.LocationId, from, to, now, ct);

        var items = steps
            .Select(WaterHeaterMapper.ToPowerItem)
            .OfType<WaterHeaterPowerItem>()
            .ToList();

        return Result.Success(new WaterHeaterPowerResponse(query.LocationId, from, to, StepSeconds, items));
    }
}
