using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Locations.Commands;
using Features.Locations.Mappers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Locations.Endpoints;

public class CreateOwnLocationEndpoint : Endpoint<CreateLocationRequest, CreatedLocationResponse>
{
    private readonly IDispatcher _dispatcher;

    public CreateOwnLocationEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Post("/api/locations");
        Tags("Locations");
        Description(b => b.WithTags("Locations"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Create my own location and its sensor key";
            s.Description = "Creates a location, links the caller to it and generates its sensor key, in one step. The full key is " +
                             "returned ONLY in this response and can never be shown again; an administrator can rotate it if it is lost.";
            s.ExampleRequest = new CreateLocationRequest("Cabin", "Hyttevegen 1", "SN-CABIN-1", "NO1");
            s.Response(201, "Location created and linked to the caller; the response contains the key, shown once");
            s.Response(400, "Invalid request (for example a zone other than NO1-NO5)");
            s.Response(401, "Not signed in");
            s.Response(409, "Another location already uses this serial number");
        });
    }

    public override async Task HandleAsync(CreateLocationRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<CreatedLocationResponse> result = await _dispatcher.Send(LocationMapper.ToOwnCommand(userId, req), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type switch
            {
                ErrorType.Conflict => 409,
                _ => 400
            });
        }

        HttpContext.Response.StatusCode = 201;
        Response = result.Value;
    }
}
