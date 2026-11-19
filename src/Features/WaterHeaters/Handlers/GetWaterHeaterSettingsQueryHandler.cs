using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.WaterHeaters;
using Domain.Common;
using Domain.Common.Entities;
using Features.WaterHeaters.Mappers;
using Features.WaterHeaters.Queries;

namespace Features.WaterHeaters.Handlers;

/// <summary>The connection of a location for an Admin. Does not call OSO and never returns the key.</summary>
public class GetWaterHeaterSettingsQueryHandler(
    ILocationRepository<Location> locations,
    IWaterHeaterRepository<WaterHeater> heaters)
    : IQueryHandler<GetWaterHeaterSettingsQuery, Result<WaterHeaterSettingsResponse>>
{
    public async Task<Result<WaterHeaterSettingsResponse>> Handle(GetWaterHeaterSettingsQuery query, CancellationToken ct)
    {
        if (await locations.GetByIdAsync(query.LocationId, ct) is null)
        {
            return Result.Failure<WaterHeaterSettingsResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        WaterHeater? heater = await heaters.GetByLocationAsync(query.LocationId, ct);
        return heater is null
            ? Result.Failure<WaterHeaterSettingsResponse>(WaterHeaterErrors.NotConnected)
            : Result.Success(WaterHeaterMapper.ToSettings(heater));
    }
}
