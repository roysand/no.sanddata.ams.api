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
        _links.CountOwnersAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(
                _rows.Count(l => l.LocationId == call.Arg<Guid>() && l.Role == LocationRole.Owner)));
    }

    private Task<Result<UserLocationChangeResponse>> LinkAs(User user, LocationRole? role = null) =>
        _link.Handle(
            role is null
                ? new LinkUserLocationCommand(user.Id, _location.Id, AdminCaller)
                : new LinkUserLocationCommand(user.Id, _location.Id, AdminCaller, role.Value),
            CancellationToken.None);

    private Task<Result<UserLocationChangeResponse>> Unlink(User user) =>
        _unlink.Handle(new UnlinkUserLocationCommand(user.Id, _location.Id, AdminCaller), CancellationToken.None);

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
        // A viewer link: unlinking the location's only owner is refused (see Unlink_TheLastOwner_IsRefused_AndTheLinkStays).
        _rows.Add(new UserLocation(_user.Id, _location.Id, LocationRole.Viewer));

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

    [Fact]
    public async Task Link_WithoutARole_CreatesAnOwner()
    {
        await LinkAs(_user);

        Assert.Equal(LocationRole.Owner, Assert.Single(_rows).Role);
    }

    [Fact]
    public async Task Link_AsViewer_CreatesAViewer()
    {
        await LinkAs(_user, LocationRole.Viewer);

        Assert.Equal(LocationRole.Viewer, Assert.Single(_rows).Role);
    }

    [Fact]
    public async Task Link_AgainWithTheSameRole_ChangesNothing()
    {
        await LinkAs(_user, LocationRole.Viewer);

        Result<UserLocationChangeResponse> again = await LinkAs(_user, LocationRole.Viewer);

        Assert.True(again.IsSuccess);
        Assert.False(again.Value.Changed);
        Assert.Single(_rows);
    }

    [Fact]
    public async Task Link_ViewerToOwner_ChangesTheRoleAndAllowsASecondOwner()
    {
        User first = _fx.AddUser("first@example.com");
        await LinkAs(first);
        await LinkAs(_user, LocationRole.Viewer);

        Result<UserLocationChangeResponse> result = await LinkAs(_user, LocationRole.Owner);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Changed);
        Assert.Equal(2, _rows.Count(l => l.Role == LocationRole.Owner));
    }

    [Fact]
    public async Task Link_DemotingTheLastOwner_IsRefused()
    {
        await LinkAs(_user);

        Result<UserLocationChangeResponse> result = await LinkAs(_user, LocationRole.Viewer);

        Assert.Equal("Location.LastOwner", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(LocationRole.Owner, Assert.Single(_rows).Role);
    }

    [Fact]
    public async Task Link_DemotingAnOwnerWhenAnotherExists_Succeeds()
    {
        User other = _fx.AddUser("other@example.com");
        await LinkAs(_user);
        await LinkAs(other);

        Result<UserLocationChangeResponse> result = await LinkAs(_user, LocationRole.Viewer);

        Assert.True(result.IsSuccess);
        Assert.Equal(LocationRole.Viewer, _rows.Single(l => l.UserId == _user.Id).Role);
        Assert.Equal(LocationRole.Owner, _rows.Single(l => l.UserId == other.Id).Role);
    }

    [Fact]
    public async Task Unlink_TheLastOwner_IsRefused_AndTheLinkStays()
    {
        await LinkAs(_user);

        Result<UserLocationChangeResponse> result = await Unlink(_user);

        Assert.Equal("Location.LastOwner", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Single(_rows);
    }

    [Fact]
    public async Task Unlink_AViewer_AndASurplusOwner_Succeed()
    {
        User viewer = _fx.AddUser("viewer@example.com");
        User surplus = _fx.AddUser("surplus@example.com");
        await LinkAs(_user);
        await LinkAs(surplus);
        await LinkAs(viewer, LocationRole.Viewer);

        Result<UserLocationChangeResponse> viewerResult = await Unlink(viewer);
        Result<UserLocationChangeResponse> ownerResult = await Unlink(surplus);

        Assert.True(viewerResult.Value.Changed);
        Assert.True(ownerResult.Value.Changed);
        Assert.Equal(_user.Id, Assert.Single(_rows).UserId);
    }

}
