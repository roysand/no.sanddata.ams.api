using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Measurements.Handlers;
using Features.Measurements.Queries;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Measurements;

public class GetMeasurementsQueryHandlerTests
{
    private readonly ILocationRepository<Location> _locationRepository = Substitute.For<ILocationRepository<Location>>();
    private readonly IMeterRepository<Meter> _meterRepository = Substitute.For<IMeterRepository<Meter>>();
    private readonly IMeasurementRepository<Measurement> _measurementRepository = Substitute.For<IMeasurementRepository<Measurement>>();
    private readonly GetMeasurementsQueryHandler _handler;

    public GetMeasurementsQueryHandlerTests()
    {
        _handler = new GetMeasurementsQueryHandler(
            _locationRepository, _meterRepository, _measurementRepository,
            Substitute.For<ILogger<GetMeasurementsQueryHandler>>());

        _locationRepository.IsUserAssociatedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
        _measurementRepository
            .GetPagedAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((Array.Empty<Measurement>(), 0));
    }

    [Fact]
    public async Task Handle_UserNotAssociatedWithLocation_ReturnsLocationNotFound()
    {
        _locationRepository.IsUserAssociatedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var query = new GetMeasurementsQuery(Guid.NewGuid(), Guid.NewGuid(), null, null, null, 1, 500);

        Result<PagedMeasurementsResponse> result = await _handler.Handle(query, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Location.NotFound", result.Error.Code);
        await _measurementRepository.DidNotReceive().GetPagedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoRangeProvided_DefaultsToLast24Hours()
    {
        var query = new GetMeasurementsQuery(Guid.NewGuid(), Guid.NewGuid(), null, null, null, 1, 500);
        DateTime before = DateTime.UtcNow;

        await _handler.Handle(query, CancellationToken.None);

        DateTime after = DateTime.UtcNow;
        await _measurementRepository.Received(1).GetPagedAsync(
            query.LocationId, null,
            Arg.Is<DateTime>(from => from >= before.AddHours(-24) && from <= after.AddHours(-24)),
            Arg.Is<DateTime>(to => to >= before && to <= after),
            1, 500, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OnlyFromProvided_ToDefaultsToNow()
    {
        var explicitFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var query = new GetMeasurementsQuery(Guid.NewGuid(), Guid.NewGuid(), null, explicitFrom, null, 1, 500);
        DateTime before = DateTime.UtcNow;

        await _handler.Handle(query, CancellationToken.None);

        DateTime after = DateTime.UtcNow;
        await _measurementRepository.Received(1).GetPagedAsync(
            query.LocationId, null, explicitFrom,
            Arg.Is<DateTime>(to => to >= before && to <= after),
            1, 500, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OnlyToProvided_FromDefaultsTo24HoursBeforeTo()
    {
        var explicitTo = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var query = new GetMeasurementsQuery(Guid.NewGuid(), Guid.NewGuid(), null, null, explicitTo, 1, 500);

        await _handler.Handle(query, CancellationToken.None);

        await _measurementRepository.Received(1).GetPagedAsync(
            query.LocationId, null, explicitTo.AddHours(-24), explicitTo, 1, 500, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LocalKindDateTimeProvided_IsNormalizedToUtcInstant()
    {
        // A Local-kind DateTime represents the same real instant as its UTC equivalent, just with
        // a different Kind label and clock value - the handler must convert it (ToUniversalTime),
        // not merely relabel it (SpecifyKind), or the wrong instant gets sent to Postgres.
        DateTime localFrom = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc).ToLocalTime();
        DateTime localTo = new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc).ToLocalTime();
        var query = new GetMeasurementsQuery(Guid.NewGuid(), Guid.NewGuid(), null, localFrom, localTo, 1, 500);

        await _handler.Handle(query, CancellationToken.None);

        await _measurementRepository.Received(1).GetPagedAsync(
            query.LocationId, null,
            Arg.Is<DateTime>(from => from == localFrom.ToUniversalTime() && from.Kind == DateTimeKind.Utc),
            Arg.Is<DateTime>(to => to == localTo.ToUniversalTime() && to.Kind == DateTimeKind.Utc),
            1, 500, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MeterNotBelongingToLocation_ReturnsMeterNotFound()
    {
        var locationId = Guid.NewGuid();
        var meterId = Guid.NewGuid();
        _meterRepository.GetByIdAsync(meterId, Arg.Any<CancellationToken>())
            .Returns(new Meter(meterId, Guid.NewGuid(), "device-1", null, true));
        var query = new GetMeasurementsQuery(Guid.NewGuid(), locationId, meterId, null, null, 1, 500);

        Result<PagedMeasurementsResponse> result = await _handler.Handle(query, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Meter.NotFound", result.Error.Code);
    }
}
