using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.ElectricityCost;
using Domain.Common;
using Domain.Common.Entities;
using Features.ElectricityCost.Logging;
using Features.ElectricityCost.Queries;
using Features.ElectricityCost.Services;
using Microsoft.Extensions.Logging;

namespace Features.ElectricityCost.Handlers;

public class GetDayPricesQueryHandler(
    ILocationRepository<Location> locationRepository,
    CostCalculator costCalculator,
    ILogger<GetDayPricesQueryHandler> logger)
    : IQueryHandler<GetDayPricesQuery, Result<DayPricesResponse>>
{
    public async Task<Result<DayPricesResponse>> Handle(GetDayPricesQuery query, CancellationToken ct)
    {
        if (!await locationRepository.IsUserAssociatedAsync(query.UserId, query.LocationId, ct))
        {
            LogMessages.LocationAccessDenied(logger, query.UserId, query.LocationId);
            return Result.Failure<DayPricesResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        Location? location = await locationRepository.GetByIdAsync(query.LocationId, ct);
        if (location is null)
        {
            return Result.Failure<DayPricesResponse>(Error.NotFound("Location.NotFound", "Location not found"));
        }

        DateTime localDate = query.Date is { } requested
            ? CostTime.LocalDate(requested)
            : CostTime.LocalDateOf(DateTime.UtcNow);
        DateTime start = CostTime.LocalDayStartUtc(localDate);
        DateTime end = CostTime.LocalDayStartUtc(localDate.AddDays(1));

        // The rate is read from a one-kWh calculation so it is exactly the rate used in the cost endpoints.
        var items = new List<HourPriceResponse>();
        string pricingModel = location.HasNorgesPriceAgreement ? nameof(PricingModel.NorgesPris) : nameof(PricingModel.Spot);
        for (DateTime hour = start; hour < end; hour = hour.AddHours(1))
        {
            CostResult cost = await costCalculator.CalculateAsync(location, hour, 1m, ct);
            pricingModel = cost.PricingModel.ToString();
            RateCost? spot = cost.PricingModel == PricingModel.Spot ? cost.Actual : cost.Comparison;
            items.Add(new HourPriceResponse(hour, cost.Actual?.RatePerKwh, spot?.RatePerKwh));
        }

        LogMessages.CostQueried(logger, query.UserId, query.LocationId);

        return Result.Success(new DayPricesResponse(DateOnly.FromDateTime(localDate), location.Zone, pricingModel, items));
    }
}
