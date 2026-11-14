using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.NorgesPris.Commands;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.NorgesPris.Endpoints;

public class DeleteNorgesPrisRateEndpoint : EndpointWithoutRequest
{
    private readonly IDispatcher _dispatcher;

    public DeleteNorgesPrisRateEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Delete("/api/admin/norgespris/{id}");
        Tags("NorgesPris");
        Description(b => b.WithTags("NorgesPris"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Delete a Norgespris rate that has not started (admin)";
            s.Description = "Only a rate whose start date is after today can be deleted. A rate that has started, " +
                             "including the one in effect, cannot be changed or deleted; add a newer rate instead.";
            s.Response(204, "Rate deleted");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "Rate not found");
            s.Response(409, "The rate has already started");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<NorgesPrisRateDeleted> result =
            await _dispatcher.Send(new DeleteNorgesPrisRateCommand(Route<Guid>("id"), userId), ct);

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
