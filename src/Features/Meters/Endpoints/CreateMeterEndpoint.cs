using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Meters.Commands;
using Features.Meters.Mappers;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Features.Meters.Endpoints;

public class CreateMeterEndpoint : Endpoint<CreateMeterRequest, MeterResponse>
{
    private readonly IDispatcher _dispatcher;

    public CreateMeterEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Post("/api/meters");
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Register a reader";
            s.Description = "Registers a new reader (meter) at a location, so it's allowed to submit measurements";
            s.ExampleRequest = new CreateMeterRequest(Guid.NewGuid(), "58:CF:79:9C:93:AE", "Main building");
            s.Response(200, "Reader registered successfully");
            s.Response(404, "Location not found");
            s.Response(409, "Reader already registered at this location");
        });
    }

    public override async Task HandleAsync(CreateMeterRequest req, CancellationToken ct)
    {
        CreateMeterCommand command = MeterMapper.ToCommand(req);
        Result<MeterResponse> result = await _dispatcher.Send(command, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
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
