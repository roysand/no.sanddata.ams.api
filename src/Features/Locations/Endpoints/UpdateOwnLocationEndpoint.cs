using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Locations.Mappers;
using Features.Locations.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Locations.Endpoints;

public class UpdateOwnLocationEndpoint : Endpoint<UpdateOwnLocationRequest, LocationSummaryResponse>
{
    private readonly IDispatcher _dispatcher;

    public UpdateOwnLocationEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Put("/api/locations/{id}");
        Tags("Locations");
        Description(b => b.WithTags("Locations"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Edit my location";
            s.Description = "An owner changes the name, address and active flag of their location. The serial number, price " +
                             "zone, Norgespris agreement and sensor key are not part of this request and cannot be changed " +
                             "here; an administrator changes them. A deactivated location stops accepting sensor readings " +
                             "and is hidden from its viewers, but stays visible to its owners so it can be switched on again.";
            s.ExampleRequest = new UpdateOwnLocationRequest(Guid.NewGuid(), "Cabin", "Hyttevegen 1", true);
            s.Response(200, "Location updated");
            s.Response(400, "Invalid request");
            s.Response(401, "Not signed in");
            s.Response(404, "Location not found, or you do not own it");
        });
    }

    public override async Task HandleAsync(UpdateOwnLocationRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<LocationSummaryResponse> result = await _dispatcher.Send(LocationMapper.ToCommand(userId, req), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type == ErrorType.NotFound ? 404 : 400);
        }

        Response = result.Value;
    }
}

/// <param name="IsActive">Required; a missing value is rejected rather than read as false.</param>
public record UpdateOwnLocationRequest(Guid Id, string Name, string Address, bool? IsActive);
