using System.Linq.Expressions;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Commands;
using Features.Users.Handlers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Users;

public class UserLocationHandlerTests
{
    private readonly UserFixture _fx = new();
    private readonly ILocationRepository<Location> _locations = Substitute.For<ILocationRepository<Location>>();
    private readonly IUserLocationRepository<UserLocation> _links = Substitute.For<IUserLocationRepository<UserLocation>>();
    private readonly List<UserLocation> _rows = [];
    private readonly Location _location = new(Guid.NewGuid(), "Home", "Addr", "SN1", "NO1", true, false);
    private readonly LinkUserLocationCommandHandler _link;
    private readonly UnlinkUserLocationCommandHandler _unlink;
    private readonly User _admin;
    private readonly User _user;

    public UserLocationHandlerTests()
    {
        _link = new LinkUserLocationCommandHandler(_fx.UserRepository, _locations, _links,
            Substitute.For<ILogger<LinkUserLocationCommandHandler>>());
        _unlink = new UnlinkUserLocationCommandHandler(_fx.UserRepository, _locations, _links,
            Substitute.For<ILogger<UnlinkUserLocationCommandHandler>>());

        _admin = _fx.AddUser("admin@example.com", isAdmin: true);
        _user = _fx.AddUser("user@example.com");

        _fx.UserRepository.ExistsAsync(Arg.Any<Expression<Func<User, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(_fx.Users.Any(call.Arg<Expression<Func<User, bool>>>().Compile())));
        _locations.ExistsAsync(Arg.Any<Expression<Func<Location, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new[] { _location }.Any(call.Arg<Expression<Func<Location, bool>>>().Compile())));
        _links.ExistsAsync(Arg.Any<Expression<Func<UserLocation, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(_rows.Any(call.Arg<Expression<Func<UserLocation, bool>>>().Compile())));
        _links.FindAsync(Arg.Any<Expression<Func<UserLocation, bool>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(call => Task.FromResult<IEnumerable<UserLocation?>>(
                _rows.Where(call.Arg<Expression<Func<UserLocation, bool>>>().Compile()).Cast<UserLocation?>().ToList()));
        _links.Insert(Arg.Do<UserLocation>(_rows.Add));
        _links.Delete(Arg.Do<UserLocation>(l => _rows.Remove(l)));
    }

    private Caller AdminCaller => new(_admin.Id, true);

    [Fact]
    public async Task Link_AddsTheLinkOnce_AndIsIdempotent()
    {
        Result<UserLocationChangeResponse> first =
            await _link.Handle(new LinkUserLocationCommand(_user.Id, _location.Id, AdminCaller), CancellationToken.None);
        Result<UserLocationChangeResponse> second =
            await _link.Handle(new LinkUserLocationCommand(_user.Id, _location.Id, AdminCaller), CancellationToken.None);

        Assert.True(first.Value.Changed);
        Assert.False(second.Value.Changed);
        Assert.Single(_rows);
    }

    [Fact]
    public async Task Link_UnknownUser_ReturnsUserNotFound()
    {
        Result<UserLocationChangeResponse> result =
            await _link.Handle(new LinkUserLocationCommand(Guid.NewGuid(), _location.Id, AdminCaller), CancellationToken.None);

        Assert.Equal("User.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Link_UnknownLocation_ReturnsLocationNotFound()
    {
        Result<UserLocationChangeResponse> result =
            await _link.Handle(new LinkUserLocationCommand(_user.Id, Guid.NewGuid(), AdminCaller), CancellationToken.None);

        Assert.Equal("Location.NotFound", result.Error.Code);
        Assert.Empty(_rows);
    }

    [Fact]
    public async Task Unlink_RemovesTheLink_AndMissingLinkIsStillSuccess()
    {
        _rows.Add(new UserLocation(_user.Id, _location.Id));

        Result<UserLocationChangeResponse> first =
            await _unlink.Handle(new UnlinkUserLocationCommand(_user.Id, _location.Id, AdminCaller), CancellationToken.None);
        Result<UserLocationChangeResponse> second =
            await _unlink.Handle(new UnlinkUserLocationCommand(_user.Id, _location.Id, AdminCaller), CancellationToken.None);

        Assert.True(first.Value.Changed);
        Assert.True(second.IsSuccess);
        Assert.False(second.Value.Changed);
        Assert.Empty(_rows);
    }

    [Fact]
    public async Task Unlink_UnknownUserOrLocation_ReturnsNotFound()
    {
        Result<UserLocationChangeResponse> noUser =
            await _unlink.Handle(new UnlinkUserLocationCommand(Guid.NewGuid(), _location.Id, AdminCaller), CancellationToken.None);
        Result<UserLocationChangeResponse> noLocation =
            await _unlink.Handle(new UnlinkUserLocationCommand(_user.Id, Guid.NewGuid(), AdminCaller), CancellationToken.None);

        Assert.Equal("User.NotFound", noUser.Error.Code);
        Assert.Equal("Location.NotFound", noLocation.Error.Code);
    }

}
