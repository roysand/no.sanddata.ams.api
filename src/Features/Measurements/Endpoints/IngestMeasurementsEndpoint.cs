using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Measurements.Commands;
using Features.Measurements.Mappers;

namespace Features.Measurements.Endpoints;

public class IngestMeasurementsEndpoint : Endpoint<IngestMeasurementsRequest, IngestMeasurementsResponse>
{
    private readonly IDispatcher _dispatcher;

    public IngestMeasurementsEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Post("/api/measurements");
        AuthSchemes("ApiKey");
        Summary(s =>
        {
            s.Summary = "Ingest power measurements";
            s.Description = "Accepts a batch of power-usage readings from a sensor/reader forwarder, authenticated via API key";
            s.ExampleRequest = new IngestMeasurementsRequest(
                "58:CF:79:9C:93:AE",
                [new MeasurementReadingRequest(1787923271, "7359992896383454", "6525", 995)]);
        });
    }

    public override async Task HandleAsync(IngestMeasurementsRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirst("LocationId")?.Value, out Guid locationId))
        {
            AddError("ApiKey.MissingLocation", "API key is not associated with a location");
            ThrowIfAnyErrors(401);
        }

        IngestMeasurementsCommand command = MeasurementMapper.ToCommand(locationId, req);
        Result<IngestMeasurementsResponse> result = await _dispatcher.Send(command, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type switch
            {
                ErrorType.NotFound => 404,
                ErrorType.Validation => 400,
                _ => 400
            });
        }

        Response = result.Value;
    }
}

public record IngestMeasurementsRequest(string DeviceId, IReadOnlyList<MeasurementReadingRequest> Readings);

public record MeasurementReadingRequest(long Timestamp, string? MeterId, string? MeterType, int PowerWatts);
