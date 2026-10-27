using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Meters.Commands;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Meters.Endpoints;

public class UpdateMeterCommentEndpoint : Endpoint<UpdateMeterCommentRequest, MeterResponse>
{
    private readonly IDispatcher _dispatcher;

    public UpdateMeterCommentEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Put("/api/meters/{id}");
        Tags("Meters");
        Description(b => b.WithTags("Meters"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Edit a reader's comment";
            s.Description = "Changes the comment of a reader. Only the owners of its location, and administrators, may do this; " +
                             "the device id and the location cannot be changed.";
            s.ExampleRequest = new UpdateMeterCommentRequest(Guid.NewGuid(), "Main building");
            s.Response(200, "Comment updated");
            s.Response(400, "Comment is longer than 200 characters");
            s.Response(401, "Not signed in");
            s.Response(404, "Reader not found, or you do not own its location");
        });
    }

    public override async Task HandleAsync(UpdateMeterCommentRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<MeterResponse> result = await _dispatcher.Send(
            new UpdateMeterCommentCommand(req.Id, userId, User.IsInRole(RoleNames.Admin), req.Comment?.Trim()), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type == ErrorType.NotFound ? 404 : 400);
        }

        Response = result.Value;
    }
}

public record UpdateMeterCommentRequest(Guid Id, string? Comment);
