using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Handlers;
using Features.Users.Queries;
using NSubstitute;

namespace Features.Tests.Users;

public class GetUsersQueryHandlerTests
{
    private readonly UserFixture _fx = new();
    private readonly IUserLocationRepository<UserLocation> _links = Substitute.For<IUserLocationRepository<UserLocation>>();
    private readonly GetUsersQueryHandler _handler;

    public GetUsersQueryHandlerTests()
    {
        _handler = new GetUsersQueryHandler(_fx.UserRepository, _links);
        _links.GetForUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<UserLinkInfo>());
    }

    [Fact]
    public async Task Handle_ShowsEachUsersLocationsWithTheRoleAndListsUsersWithNone()
    {
        User owner = _fx.AddUser("owner@example.com");
        User viewer = _fx.AddUser("viewer@example.com");
        User nobody = _fx.AddUser("nobody@example.com");
        var cabin = Guid.NewGuid();
        var office = Guid.NewGuid();
        _links.GetForUsersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([
                new UserLinkInfo(owner.Id, cabin, "Cabin", LocationRole.Owner),
                new UserLinkInfo(owner.Id, office, "Office", LocationRole.Viewer),
                new UserLinkInfo(viewer.Id, cabin, "Cabin", LocationRole.Viewer)
            ]);

        Result<PagedUsersResponse> result = await _handler.Handle(new GetUsersQuery(), CancellationToken.None);

        UserListResponse[] users = result.Value.Users;
        Assert.Equal(3, users.Length);
        UserListResponse ownerResponse = users.Single(u => u.Id == owner.Id);
        Assert.Equal(2, ownerResponse.LocationAccess.Length);
        Assert.Equal("Owner", ownerResponse.LocationAccess.Single(a => a.LocationId == cabin).Role);
        Assert.Equal("Viewer", ownerResponse.LocationAccess.Single(a => a.LocationId == office).Role);
        Assert.Equal("Viewer", users.Single(u => u.Id == viewer.Id).LocationAccess.Single().Role);
        Assert.Empty(users.Single(u => u.Id == nobody.Id).LocationAccess);
    }

    [Fact]
    public async Task Handle_AsksForTheRolesOfOnlyTheUsersOnThePage_InOneQuery()
    {
        for (int i = 0; i < 5; i++)
        {
            _fx.AddUser($"user{i}@example.com");
        }

        await _handler.Handle(new GetUsersQuery(PageNumber: 1, PageSize: 2), CancellationToken.None);

        await _links.Received(1).GetForUsersAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_KeepsTheExistingLocationFields()
    {
        _fx.AddUser("someone@example.com");

        Result<PagedUsersResponse> result = await _handler.Handle(new GetUsersQuery(), CancellationToken.None);

        UserListResponse user = Assert.Single(result.Value.Users);
        Assert.NotNull(user.Locations);
        Assert.NotNull(user.LocationIds);
    }
}
