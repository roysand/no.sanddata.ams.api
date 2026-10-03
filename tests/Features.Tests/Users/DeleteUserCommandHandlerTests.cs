using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Commands;
using Features.Users.Handlers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Users;

public class DeleteUserCommandHandlerTests
{
    private readonly UserFixture _fx = new();
    private readonly DeleteUserCommandHandler _handler;

    public DeleteUserCommandHandlerTests() =>
        _handler = new DeleteUserCommandHandler(_fx.UserRepository, _fx.RoleRepository, _fx.UserRoleRepository,
            Substitute.For<ILogger<DeleteUserCommandHandler>>());

    [Fact]
    public async Task Handle_OrdinaryUser_IsDeleted()
    {
        User admin = _fx.AddUser("admin@example.com", isAdmin: true);
        User other = _fx.AddUser("other@example.com");

        Result<DeleteUserResponse> result =
            await _handler.Handle(new DeleteUserCommand(other.Id, new Caller(admin.Id, true)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(other, _fx.Deleted);
    }

    [Fact]
    public async Task Handle_LastActiveAdmin_ReturnsLastAdminConflictAndDeletesNothing()
    {
        User admin = _fx.AddUser("admin@example.com", isAdmin: true);
        _fx.AddUser("other@example.com");

        Result<DeleteUserResponse> result =
            await _handler.Handle(new DeleteUserCommand(admin.Id, new Caller(admin.Id, true)), CancellationToken.None);

        Assert.Equal("User.LastAdmin", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(_fx.Deleted);
    }

    [Fact]
    public async Task Handle_AdminWhenAnotherActiveAdminExists_IsDeleted()
    {
        User first = _fx.AddUser("a1@example.com", isAdmin: true);
        _fx.AddUser("a2@example.com", isAdmin: true);

        Result<DeleteUserResponse> result =
            await _handler.Handle(new DeleteUserCommand(first.Id, new Caller(first.Id, true)), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Handle_AdminWhenTheOtherAdminIsInactive_IsStillProtected()
    {
        User first = _fx.AddUser("a1@example.com", isAdmin: true);
        _fx.AddUser("a2@example.com", isActive: false, isAdmin: true);

        Result<DeleteUserResponse> result =
            await _handler.Handle(new DeleteUserCommand(first.Id, new Caller(first.Id, true)), CancellationToken.None);

        Assert.Equal("User.LastAdmin", result.Error.Code);
    }

    [Fact]
    public async Task Handle_UnknownUser_ReturnsNotFound()
    {
        User admin = _fx.AddUser("admin@example.com", isAdmin: true);

        Result<DeleteUserResponse> result =
            await _handler.Handle(new DeleteUserCommand(Guid.NewGuid(), new Caller(admin.Id, true)), CancellationToken.None);

        Assert.Equal("User.NotFound", result.Error.Code);
    }
}
