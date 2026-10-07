using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Locations.Mappers;
using Features.Locations.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Locations.Endpoints;

public class UpdateLocationEndpoint : Endpoint<UpdateLocationRequest, AdminLocationResponse>
{
    private readonly IDispatcher _dispatcher;

    public UpdateLocationEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Put("/api/admin/locations/{id}");
        Tags("Locations");
        Description(b => b.WithTags("Locations"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Edit a location (admin)";
            s.Description = "Changes the details and the active flag; the sensor key is not touched. A deactivated location " +
                             "rejects sensor readings and is hidden from regular users. Changing the zone or the Norgespris " +
                             "flag changes how past hours are priced, because costs are always calculated from current settings.";
            s.Response(200, "Location updated");
            s.Response(400, "Invalid request");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "Location not found");
            s.Response(409, "Another location already uses this serial number");
        });
    }

    public override async Task HandleAsync(UpdateLocationRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<AdminLocationResponse> result = await _dispatcher.Send(LocationMapper.ToCommand(userId, req), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type switch
            {
                ErrorType.NotFound => 404,
                ErrorType.Conflict => 409,
                _ => 400
            });
        }

        Response = result.Value;
    }
}

public record UpdateLocationRequest(
    Guid Id,
    string Name,
    string Address,
    string SerialNumber,
    string Zone,
    bool HasNorgesPriceAgreement,
    bool IsActive);
