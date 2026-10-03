using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Commands;
using Features.Users.Handlers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Users;

public class UpdateUserCommandHandlerTests
{
    private readonly UserFixture _fx = new();
    private readonly UpdateUserCommandHandler _handler;

    public UpdateUserCommandHandlerTests() =>
        _handler = new UpdateUserCommandHandler(_fx.UserRepository, _fx.RoleRepository, _fx.UserRoleRepository,
            Substitute.For<ILogger<UpdateUserCommandHandler>>());

    private static UpdateUserCommand Cmd(User target, Caller caller, string? email = null, bool? isActive = null) =>
        new(target.Id, "New", "Name", email ?? target.Email.Value, isActive ?? target.IsActive, caller);

    [Fact]
    public async Task Handle_OwnAccount_UpdatesNames()
    {
        User me = _fx.AddUser("me@example.com");

        Result<UpdateUserResponse> result = await _handler.Handle(Cmd(me, new Caller(me.Id, false)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("New", me.FirstName);
        await _fx.UserRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OtherAccountAsNonAdmin_ReturnsNotFoundAndChangesNothing()
    {
        User me = _fx.AddUser("me@example.com");
        User other = _fx.AddUser("other@example.com");

        Result<UpdateUserResponse> result = await _handler.Handle(Cmd(other, new Caller(me.Id, false)), CancellationToken.None);

        Assert.Equal("User.NotFound", result.Error.Code);
        Assert.Equal("First", other.FirstName);
        await _fx.UserRepository.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NonAdminChangesOwnIsActive_IsRejected()
    {
        User me = _fx.AddUser("me@example.com");

        Result<UpdateUserResponse> result =
            await _handler.Handle(Cmd(me, new Caller(me.Id, false), isActive: false), CancellationToken.None);

        Assert.Equal("User.IsActiveAdminOnly", result.Error.Code);
        Assert.True(me.IsActive);
    }

    [Fact]
    public async Task Handle_AdminDeactivatesAnotherUser_Succeeds()
    {
        User admin = _fx.AddUser("admin@example.com", isAdmin: true);
        User other = _fx.AddUser("other@example.com");

        Result<UpdateUserResponse> result =
            await _handler.Handle(Cmd(other, new Caller(admin.Id, true), isActive: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(other.IsActive);
    }

    [Fact]
    public async Task Handle_DeactivatingTheLastActiveAdmin_ReturnsLastAdminConflict()
    {
        User admin = _fx.AddUser("admin@example.com", isAdmin: true);

        Result<UpdateUserResponse> result =
            await _handler.Handle(Cmd(admin, new Caller(admin.Id, true), isActive: false), CancellationToken.None);

        Assert.Equal("User.LastAdmin", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.True(admin.IsActive);
    }

    [Fact]
    public async Task Handle_DeactivatingAnAdminWhenAnotherActiveAdminExists_Succeeds()
    {
        User first = _fx.AddUser("a1@example.com", isAdmin: true);
        _fx.AddUser("a2@example.com", isAdmin: true);

        Result<UpdateUserResponse> result =
            await _handler.Handle(Cmd(first, new Caller(first.Id, true), isActive: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_EmailTakenByAnotherUser_ReturnsConflict()
    {
        User me = _fx.AddUser("me@example.com");
        _fx.AddUser("taken@example.com");

        Result<UpdateUserResponse> result =
            await _handler.Handle(Cmd(me, new Caller(me.Id, false), email: "taken@example.com"), CancellationToken.None);

        Assert.Equal("User.EmailExists", result.Error.Code);
    }
}
