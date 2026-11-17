using System.Globalization;
using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.ElectricityCost.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.ElectricityCost.Endpoints;

/// <param name="Month">yyyy-MM; the current Oslo month when omitted.</param>
public record GetCapacityChargeRequest(Guid LocationId, string? Month = null);

public class GetCapacityChargeEndpoint : Endpoint<GetCapacityChargeRequest, CapacityChargeResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetCapacityChargeEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/electricity-cost/capacity");
        Tags("ElectricityCost");
        Description(b => b.WithTags("ElectricityCost"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Get the monthly grid capacity charge (kapasitetsledd)";
            s.Description = "The grid company finds the hour with the highest consumption on each Oslo day, averages the three " +
                             "highest of those on different days (fewer when fewer days have data), and the step that holds " +
                             "the average gives the monthly amount. Returns the peaks, the average, the step, the amount " +
                             "excluding and including VAT, and how far the average is below the next step. provisional is " +
                             "true until the month has ended. set is false, with the figures null, when the location has no " +
                             "grid tariff, the tariff has no step table for the month, or there is no consumption. A monthly " +
                             "figure: it is not part of the hourly or daily costs.";
            s.Response(200, "Capacity charge retrieved successfully");
            s.Response(400, "Malformed month");
            s.Response(404, "Location not found");
        });
    }

    public override async Task HandleAsync(GetCapacityChargeRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        // The validator has checked the format; null means the current month, decided by the handler.
        DateOnly? month = req.Month is { } text
            ? DateOnly.ParseExact(text + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

        Result<CapacityChargeResponse> result =
            await _dispatcher.Send(new GetCapacityChargeQuery(userId, req.LocationId, month), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type == ErrorType.NotFound ? 404 : 400);
        }

        Response = result.Value;
    }
}
