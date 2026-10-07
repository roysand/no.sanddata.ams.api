using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Locations.Commands;
using Features.Locations.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Locations.Endpoints;

public class SetLocationKeyActiveEndpoint : Endpoint<SetLocationKeyActiveRequest, ApiKeyInfoResponse>
{
    private readonly IDispatcher _dispatcher;

    public SetLocationKeyActiveEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Put("/api/admin/locations/{id}/api-key");
        Tags("Locations");
        Description(b => b.WithTags("Locations"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Activate or deactivate a location's sensor key (admin)";
            s.Description = "A deactivated key is rejected. The key itself does not change; rotate it to get a new one.";
            s.ExampleRequest = new SetLocationKeyActiveRequest(Guid.NewGuid(), false);
            s.Response(200, "Key state updated");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "Location not found");
        });
    }

    public override async Task HandleAsync(SetLocationKeyActiveRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<ApiKeyInfoResponse> result =
            await _dispatcher.Send(new SetLocationKeyActiveCommand(userId, req.Id, req.IsActive), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type == ErrorType.NotFound ? 404 : 400);
        }

        Response = result.Value;
    }
}

public record SetLocationKeyActiveRequest(Guid Id, bool IsActive);
