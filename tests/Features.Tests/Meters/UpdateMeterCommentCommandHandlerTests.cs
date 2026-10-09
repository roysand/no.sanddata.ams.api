using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Meters.Commands;
using Features.Meters.Handlers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Meters;

public class UpdateMeterCommentCommandHandlerTests
{
    private readonly IMeterRepository<Meter> _meters = Substitute.For<IMeterRepository<Meter>>();
    private readonly IUserLocationRepository<UserLocation> _links = Substitute.For<IUserLocationRepository<UserLocation>>();
    private readonly UpdateMeterCommentCommandHandler _handler;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Meter _meter = new(Guid.NewGuid(), Guid.NewGuid(), "dev1", "Old comment", isActive: true);

    public UpdateMeterCommentCommandHandlerTests()
    {
        _handler = new UpdateMeterCommentCommandHandler(
            _meters, _links, Substitute.For<ILogger<UpdateMeterCommentCommandHandler>>());
        _meters.GetByIdAsync(_meter.Id, Arg.Any<CancellationToken>()).Returns(_meter);
    }

    private void Owns(bool isOwner) =>
        _links.IsOwnerAsync(_userId, _meter.LocationId, Arg.Any<CancellationToken>()).Returns(isOwner);

    private UpdateMeterCommentCommand Command(string? comment = "New comment", bool isAdmin = false, Guid? meterId = null) =>
        new(meterId ?? _meter.Id, _userId, isAdmin, comment);

    [Fact]
    public async Task Handle_Owner_ChangesOnlyTheComment()
    {
        Owns(true);

        Result<MeterResponse> result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New comment", result.Value.Comment);
        Assert.Equal("New comment", _meter.Comment);
        Assert.Equal("dev1", _meter.DeviceId);
        Assert.Equal(_meter.LocationId, result.Value.LocationId);
        await _meters.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NullComment_ClearsIt()
    {
        Owns(true);

        Result<MeterResponse> result = await _handler.Handle(Command(comment: null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(_meter.Comment);
    }

    [Fact]
    public async Task Handle_AdminWhoOwnsNothing_CanStillEdit()
    {
        Owns(false);

        Result<MeterResponse> result = await _handler.Handle(Command(isAdmin: true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New comment", _meter.Comment);
    }

    [Fact]
    public async Task Handle_ViewerOrStranger_GetsNotFound_AndNothingIsSaved()
    {
        Owns(false);

        Result<MeterResponse> result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.Equal("Meter.NotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal("Old comment", _meter.Comment);
        await _meters.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MissingMeter_GetsTheSameAnswerAsAViewer()
    {
        Owns(true);

        Result<MeterResponse> result = await _handler.Handle(Command(meterId: Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("Meter.NotFound", result.Error.Code);
    }
}
