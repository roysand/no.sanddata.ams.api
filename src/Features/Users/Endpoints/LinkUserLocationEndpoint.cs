using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Users.Commands;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Users.Endpoints;

public class LinkUserLocationEndpoint : EndpointWithoutRequest
{
    private readonly IDispatcher _dispatcher;

    public LinkUserLocationEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Put("/api/users/{id}/locations/{locationId}");
        Tags("Users");
        Description(b => b.WithTags("Users"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Link a user to a location";
            s.Description = "Lets the user see the location's data. Idempotent.";
            s.Response(204, "User linked (or already linked)");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "User or location not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        Result<UserLocationChangeResponse> result = await _dispatcher.Send(
            new LinkUserLocationCommand(Route<Guid>("id"), Route<Guid>("locationId"), Caller.From(User)), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type == ErrorType.NotFound ? 404 : 400);
        }

        HttpContext.Response.StatusCode = 204;
    }
}

