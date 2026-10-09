using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Users.Commands;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Users.Endpoints;

public class LinkUserLocationEndpoint : Endpoint<LinkUserLocationRequest>
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
            s.Description = "Lets the user see the location's data. The optional body { \"role\": \"Owner\" | \"Viewer\" } " +
                             "sets what the user may do there; without a role the user becomes an owner. Idempotent; " +
                             "linking again with a different role changes the role.";
            s.Response(204, "User linked, or the role changed, or nothing to change");
            s.Response(400, "Role is not Owner or Viewer");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "User or location not found");
            s.Response(409, "The change would leave the location without an owner");
        });
    }

    public override async Task HandleAsync(LinkUserLocationRequest req, CancellationToken ct)
    {
        // The validator has already rejected anything that is not a role name.
        LocationRole role = string.IsNullOrWhiteSpace(req.Role)
            ? LocationRole.Owner
            : Enum.Parse<LocationRole>(req.Role.Trim(), ignoreCase: true);

        Result<UserLocationChangeResponse> result = await _dispatcher.Send(
            new LinkUserLocationCommand(req.Id, req.LocationId, Caller.From(User), role), ct);

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

        HttpContext.Response.StatusCode = 204;
    }
}

/// <summary>Id and LocationId come from the route; Role from the optional body.</summary>
public class LinkUserLocationRequest
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public string? Role { get; set; }
}
