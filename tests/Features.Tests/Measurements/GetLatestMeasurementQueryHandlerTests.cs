using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Measurements.Handlers;
using Features.Measurements.Queries;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Measurements;

public class GetLatestMeasurementQueryHandlerTests
{
    private readonly ILocationRepository<Location> _locationRepository = Substitute.For<ILocationRepository<Location>>();
    private readonly IMeterRepository<Meter> _meterRepository = Substitute.For<IMeterRepository<Meter>>();
    private readonly IMeasurementRepository<Measurement> _measurementRepository = Substitute.For<IMeasurementRepository<Measurement>>();
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly GetLatestMeasurementQueryHandler _handler;

    public GetLatestMeasurementQueryHandlerTests()
    {
        _handler = new GetLatestMeasurementQueryHandler(
            _locationRepository, _meterRepository, _measurementRepository,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Measurements:StaleAfterSeconds"] = "60" }).Build(),
            new FixedTimeProvider(Now),
            Substitute.For<ILogger<GetLatestMeasurementQueryHandler>>());

        _locationRepository.IsUserAssociatedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    [Fact]
    public async Task Handle_NoMeasurementExists_ReturnsSuccessWithNullMeasurement()
    {
        _measurementRepository
            .GetLatestAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns((Measurement?)null);
        var query = new GetLatestMeasurementQuery(Guid.NewGuid(), Guid.NewGuid(), null);

        Result<LatestMeasurementResponse> result = await _handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Measurement);
    }

    [Fact]
    public async Task Handle_MeasurementExists_ReturnsItMapped()
    {
        var meterId = Guid.NewGuid();
        var measurement = new Measurement(Guid.NewGuid(), Guid.NewGuid(), meterId, Now.UtcDateTime.AddSeconds(-5), 1024);
        _measurementRepository
            .GetLatestAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(measurement);
        var query = new GetLatestMeasurementQuery(Guid.NewGuid(), Guid.NewGuid(), null);

        Result<LatestMeasurementResponse> result = await _handler.Handle(query, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Measurement);
        Assert.Equal(1024, result.Value.Measurement!.PowerWatts);
        Assert.Equal(meterId, result.Value.Measurement.MeterId);
    }

    [Fact]
    public async Task Handle_UserNotAssociatedWithLocation_ReturnsLocationNotFound()
    {
        _locationRepository.IsUserAssociatedAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var query = new GetLatestMeasurementQuery(Guid.NewGuid(), Guid.NewGuid(), null);

        Result<LatestMeasurementResponse> result = await _handler.Handle(query, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Location.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_MeasurementNewerThanThreshold_IsNotStale()
    {
        _measurementRepository
            .GetLatestAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new Measurement(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.UtcDateTime.AddSeconds(-59), 500));
        var query = new GetLatestMeasurementQuery(Guid.NewGuid(), Guid.NewGuid(), null);

        Result<LatestMeasurementResponse> result = await _handler.Handle(query, CancellationToken.None);

        Assert.False(result.Value.Measurement!.IsStale);
    }

    [Fact]
    public async Task Handle_MeasurementOlderThanThreshold_IsStale()
    {
        _measurementRepository
            .GetLatestAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new Measurement(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now.UtcDateTime.AddSeconds(-61), 500));
        var query = new GetLatestMeasurementQuery(Guid.NewGuid(), Guid.NewGuid(), null);

        Result<LatestMeasurementResponse> result = await _handler.Handle(query, CancellationToken.None);

        Assert.True(result.Value.Measurement!.IsStale);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
