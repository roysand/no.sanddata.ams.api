using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Locations.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Locations.Endpoints;

public class GetKeyRemindersEndpoint : EndpointWithoutRequest<KeyRemindersResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetKeyRemindersEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/admin/key-reminders");
        Tags("Locations");
        Description(b => b.WithTags("Locations"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "List the keys that expire soon (admin)";
            s.Description = "The sensor keys and water heater (OSO) keys of active locations that expire within 30 " +
                             "days or have expired, soonest first, counted in whole Oslo days. Deactivated sensor " +
                             "keys are left out. No key, hash or device id is returned.";
            s.Response(200, "The due keys; items is empty when nothing is due");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<KeyRemindersResponse> result = await _dispatcher.Send(new GetKeyRemindersQuery(userId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(400);
        }

        Response = result.Value;
    }
}
