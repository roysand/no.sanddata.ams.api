using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Measurements.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Measurements.Endpoints;

public class GetLatestMeasurementEndpoint : Endpoint<GetLatestMeasurementRequest, MeasurementResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetLatestMeasurementEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/measurements/latest");
        Tags("Measurements");
        Description(b => b.WithTags("Measurements"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Get the latest measurement for a location";
            s.Description = "Returns the single most recent power reading for one of the caller's own " +
                             "locations, optionally narrowed to a meter. 204 if no reading exists yet.";
            s.Response(200, "Latest measurement retrieved successfully");
            s.Response(204, "No measurement exists yet");
            s.Response(404, "Location or meter not found");
        });
    }

    public override async Task HandleAsync(GetLatestMeasurementRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        var query = new GetLatestMeasurementQuery(userId, req.LocationId, req.MeterId);
        Result<LatestMeasurementResponse> result = await _dispatcher.Send(query, ct);

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

        if (result.Value.Measurement is null)
        {
            await HttpContext.Response.SendNoContentAsync(ct);
            return;
        }

        Response = result.Value.Measurement;
    }
}

public record GetLatestMeasurementRequest(Guid LocationId, Guid? MeterId = null);
