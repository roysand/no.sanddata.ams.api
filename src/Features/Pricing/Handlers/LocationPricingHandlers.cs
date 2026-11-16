using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Pricing.Commands;
using Features.Pricing.Logging;
using Features.Pricing.Mappers;
using Features.Pricing.Queries;
using Microsoft.Extensions.Logging;

namespace Features.Pricing.Handlers;

public class SetLocationPricingCommandHandler(
    ILocationRepository<Location> locationRepository,
    IPriceRateRepository rates,
    ILogger<SetLocationPricingCommandHandler> logger)
    : ICommandHandler<SetLocationPricingCommand, Result<LocationPricingResponse>>
{
    public async Task<Result<LocationPricingResponse>> Handle(SetLocationPricingCommand command, CancellationToken ct)
    {
        Location? location = await locationRepository.GetByIdAsync(command.LocationId, ct);
        if (location is null)
        {
            LogMessages.Refused(logger, "LocationNotFound", command.ActingUserId);
            return Result.Failure<LocationPricingResponse>(
                Error.NotFound("Pricing.LocationNotFound", "Location not found"));
        }

        GridTariff? tariff = null;
        if (command.GridTariffId is { } tariffId)
        {
            tariff = await rates.GetGridTariffByIdAsync(tariffId, ct);
            if (tariff is null)
            {
                LogMessages.Refused(logger, "TariffNotFound", command.ActingUserId);
                return Result.Failure<LocationPricingResponse>(
                    Error.NotFound("Pricing.TariffNotFound", "Grid tariff not found"));
            }
        }

        // Changing these recalculates every past hour of the location, as the Norgespris agreement flag does.
        location.SetPricing(command.GridTariffId, command.SupplierMarkupPerKwh);
        await locationRepository.SaveChangesAsync(ct);

        LogMessages.LocationPricingChanged(
            logger, location.Id, location.GridTariffId, location.SupplierMarkupPerKwh, command.ActingUserId);

        return Result.Success(PricingMapper.ToLocationPricingResponse(location, tariff?.Name));
    }
}

public class ListLocationPricingQueryHandler(
    ILocationRepository<Location> locationRepository,
    IPriceRateRepository rates,
    ILogger<ListLocationPricingQueryHandler> logger)
    : IQueryHandler<ListLocationPricingQuery, Result<IReadOnlyList<LocationPricingResponse>>>
{
    public async Task<Result<IReadOnlyList<LocationPricingResponse>>> Handle(
        ListLocationPricingQuery query, CancellationToken ct)
    {
        IReadOnlyList<Location> locations = await locationRepository.GetAllWithKeyAsync(ct);
        IReadOnlyList<GridTariff> tariffs = await rates.GetGridTariffsAsync(ct);
        var names = tariffs.ToDictionary(t => t.Id, t => t.Name);

        var responses = locations
            .OrderBy(l => l.Name)
            .Select(l => PricingMapper.ToLocationPricingResponse(
                l, l.GridTariffId is { } id ? names.GetValueOrDefault(id) : null))
            .ToList();
        LogMessages.Listed(logger, "location pricing", query.ActingUserId, responses.Count);

        return Result.Success<IReadOnlyList<LocationPricingResponse>>(responses);
    }
}
