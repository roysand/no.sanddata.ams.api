using System.Globalization;
using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.WaterHeaters.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.WaterHeaters.Endpoints;

/// <param name="LocationId">The location, from the route.</param>
/// <param name="Period"><c>day</c>, <c>week</c>, <c>month</c> or <c>year</c>, any case; <c>day</c> when left out.</param>
/// <param name="Date">An Oslo calendar date as <c>yyyy-MM-dd</c>; today in Oslo when left out. The period is the one
/// that holds it.</param>
public record GetWaterHeaterHotWaterRequest(Guid LocationId, string? Period, string? Date);

public class GetWaterHeaterHotWaterEndpoint : Endpoint<GetWaterHeaterHotWaterRequest, WaterHeaterHotWaterResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetWaterHeaterHotWaterEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/locations/{locationId}/water-heater/hot-water");
        Tags("WaterHeater");
        Description(b => b.WithTags("WaterHeater"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Litres of 40 °C water used per hour, day or month";
            s.Description = "Counted from the stored heater samples, for the Oslo day, ISO week, month or year that " +
                             "holds date (today when left out). A day has one item per hour, a week and a month one " +
                             "per day, and a year one per month; parts that start after now have no item. from and " +
                             "to are the whole period in UTC. litres is null when the part has no sampled time (draw " +
                             "a gap, never 0); sampledMinutes says how much of it was sampled. totalLitres is the " +
                             "sum of the items' litres, null when none has any. No connected heater is needed. For " +
                             "owners and viewers of the location, and Admins.";
            s.Response(200, "The period's parts");
            s.Response(400, "period is not day, week, month or year, or date is not a real yyyy-MM-dd date");
            s.Response(401, "Not signed in");
            s.Response(404, "Location not found or not visible (Location.NotFound)");
        });
    }

    public override async Task HandleAsync(GetWaterHeaterHotWaterRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        // Both parse safely: the validator has already refused bad values.
        HotWaterPeriod period = Enum.Parse<HotWaterPeriod>(req.Period ?? nameof(HotWaterPeriod.Day), ignoreCase: true);
        DateOnly? date = req.Date is null
            ? null
            : DateOnly.ParseExact(req.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        Result<WaterHeaterHotWaterResponse> result = await _dispatcher.Send(
            new GetWaterHeaterHotWaterQuery(userId, User.IsInRole(RoleNames.Admin), req.LocationId, period, date),
            ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(WaterHeaterErrorStatus.Of(result.Error, onConnect: false));
        }

        Response = result.Value;
    }
}
