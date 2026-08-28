using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Meters.Commands;
using Features.Meters.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Features.Meters.Endpoints;

public class GetMeterEndpoint : Endpoint<GetMeterRequest, MeterResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetMeterEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/meters/{id}");
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Get a reader by id";
            s.Response(200, "Reader found");
            s.Response(404, "Reader not found");
        });
    }

    public override async Task HandleAsync(GetMeterRequest req, CancellationToken ct)
    {
        Result<MeterResponse> result = await _dispatcher.Send(new GetMeterQuery(req.Id), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors(404);
        }

        Response = result.Value;
    }
}

public record GetMeterRequest(Guid Id);
