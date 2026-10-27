using System.Text.Json;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Users.Commands;
using FluentValidation.Results;
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
        // The body is optional: without this, routing answers 415 to a PUT that has no JSON Content-Type.
        Description(b => b.WithTags("Users").ClearDefaultAccepts());
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        RequestBinder(new LinkUserLocationBinder());
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

/// <summary>
/// Binds the route values and the OPTIONAL body. The default binder answers 415 to a body-less PUT without a JSON
/// Content-Type, but linking a user without a role (an owner) must keep working that way.
/// </summary>
public class LinkUserLocationBinder : IRequestBinder<LinkUserLocationRequest>
{
    public async ValueTask<LinkUserLocationRequest> BindAsync(BinderContext ctx, CancellationToken ct)
    {
        HttpRequest request = ctx.HttpContext.Request;
        var result = new LinkUserLocationRequest();

        if (Guid.TryParse(request.RouteValues["id"]?.ToString(), out Guid id))
            result.Id = id;
        else
            ctx.ValidationFailures.Add(new ValidationFailure("id", "A valid user id is required"));

        if (Guid.TryParse(request.RouteValues["locationId"]?.ToString(), out Guid locationId))
            result.LocationId = locationId;
        else
            ctx.ValidationFailures.Add(new ValidationFailure("locationId", "A valid location id is required"));

        // No Content-Length header does not mean no body (chunked), so read it and treat an empty one as "no role".
        using var reader = new StreamReader(request.Body);
        string json = await reader.ReadToEndAsync(ct);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                result.Role = JsonSerializer.Deserialize<LinkUserLocationBody>(json, JsonSerializerOptions.Web)?.Role;
            }
            catch (JsonException)
            {
                ctx.ValidationFailures.Add(new ValidationFailure("role", "The request body is not valid JSON"));
            }
        }

        return result;
    }

    private sealed record LinkUserLocationBody(string? Role);
}
