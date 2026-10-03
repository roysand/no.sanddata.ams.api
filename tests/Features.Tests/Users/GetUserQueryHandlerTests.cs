using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Commands;
using Features.Users.Handlers;
using Features.Users.Queries;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Users;

public class GetUserQueryHandlerTests
{
    private readonly UserFixture _fx = new();
    private readonly GetUserQueryHandler _handler;

    public GetUserQueryHandlerTests() =>
        _handler = new GetUserQueryHandler(_fx.UserRepository, Substitute.For<ILogger<GetUserQueryHandler>>());

    [Fact]
    public async Task Handle_OwnAccount_Succeeds()
    {
        User me = _fx.AddUser("me@example.com");

        Result<GetUserResponse> result =
            await _handler.Handle(new GetUserQuery(me.Id, new Caller(me.Id, false)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("me@example.com", result.Value.Email);
    }

    [Fact]
    public async Task Handle_OtherAccountAsNonAdmin_LooksLikeAMissingAccount()
    {
        User me = _fx.AddUser("me@example.com");
        User other = _fx.AddUser("other@example.com");

        Result<GetUserResponse> existing =
            await _handler.Handle(new GetUserQuery(other.Id, new Caller(me.Id, false)), CancellationToken.None);
        Result<GetUserResponse> missing =
            await _handler.Handle(new GetUserQuery(Guid.NewGuid(), new Caller(me.Id, false)), CancellationToken.None);

        Assert.Equal("User.NotFound", existing.Error.Code);
        Assert.Equal(missing.Error.Code, existing.Error.Code);
        Assert.Equal(ErrorType.NotFound, existing.Error.Type);
    }

    [Fact]
    public async Task Handle_AdminViewsAnyAccount()
    {
        User admin = _fx.AddUser("admin@example.com", isAdmin: true);
        User other = _fx.AddUser("other@example.com");

        Result<GetUserResponse> result =
            await _handler.Handle(new GetUserQuery(other.Id, new Caller(admin.Id, true)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(other.Id, result.Value.Id);
    }

    [Fact]
    public async Task Handle_AdminAsksForMissingAccount_ReturnsNotFound()
    {
        User admin = _fx.AddUser("admin@example.com", isAdmin: true);

        Result<GetUserResponse> result =
            await _handler.Handle(new GetUserQuery(Guid.NewGuid(), new Caller(admin.Id, true)), CancellationToken.None);

        Assert.Equal("User.NotFound", result.Error.Code);
    }
}
