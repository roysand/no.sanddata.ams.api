using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Measurements.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Measurements.Endpoints;

public class GetMeasurementsEndpoint : Endpoint<GetMeasurementsRequest, PagedMeasurementsResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetMeasurementsEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/measurements");
        Tags("Measurements");
        Description(b => b.WithTags("Measurements"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Get measurements for a location";
            s.Description = "Returns raw power readings for one of the caller's own locations, optionally " +
                             "narrowed to a meter and/or an explicit time range. Defaults to the last 24 hours.";
            s.Response(200, "Measurements retrieved successfully");
            s.Response(400, "Invalid request parameters");
            s.Response(404, "Location or meter not found");
        });
    }

    public override async Task HandleAsync(GetMeasurementsRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var query = new GetMeasurementsQuery(userId, req.LocationId, req.MeterId, req.From, req.To, req.Page, req.PageSize);
        Result<PagedMeasurementsResponse> result = await _dispatcher.Send(query, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type switch
            {
                ErrorType.NotFound => 404,
                ErrorType.Validation => 400,
                _ => 400
            });
        }

        Response = result.Value;
    }
}

public record GetMeasurementsRequest(
    Guid LocationId,
    Guid? MeterId = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 500);
