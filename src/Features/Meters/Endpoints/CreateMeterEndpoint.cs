using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Meters.Commands;
using Features.Meters.Mappers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Meters.Endpoints;

public class CreateMeterEndpoint : Endpoint<CreateMeterRequest, MeterResponse>
{
    private readonly IDispatcher _dispatcher;

    public CreateMeterEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Post("/api/meters");
        Tags("Meters");
        Description(b => b.WithTags("Meters"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Register a reader";
            s.Description = "Registers a new reader (meter) at a location, so it is allowed to submit measurements. Linked users can register at their own active locations; administrators at any location.";
            s.ExampleRequest = new CreateMeterRequest(Guid.NewGuid(), "58:CF:79:9C:93:AE", "Main building");
            s.Response(200, "Reader registered successfully");
            s.Response(401, "Not signed in");
            s.Response(404, "Location not found, or you are not linked to it");
            s.Response(409, "Reader already registered at this location");
        });
    }

    public override async Task HandleAsync(CreateMeterRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        CreateMeterCommand command = MeterMapper.ToCommand(userId, User.IsInRole(RoleNames.Admin), req);
        Result<MeterResponse> result = await _dispatcher.Send(command, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type switch
            {
                ErrorType.NotFound => 404,
                ErrorType.Conflict => 409,
                ErrorType.Validation => 400,
                _ => 400
            });
        }

        Response = result.Value;
    }
}

public record CreateMeterRequest(Guid LocationId, string DeviceId, string? Comment);
