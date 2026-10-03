using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Commands;
using Features.Users.Handlers;
using Infrastructure.Authentication;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Users;

public class ChangePasswordCommandHandlerTests
{
    private readonly UserFixture _fx = new();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ChangePasswordCommandHandler _handler;

    public ChangePasswordCommandHandlerTests()
    {
        _handler = new ChangePasswordCommandHandler(_fx.UserRepository, _hasher,
            Substitute.For<ILogger<ChangePasswordCommandHandler>>());
        _hasher.VerifyPassword("current", Arg.Any<string>()).Returns(true);
        _hasher.VerifyPassword(Arg.Is<string>(p => p != "current"), Arg.Any<string>()).Returns(false);
        _hasher.HashPassword("NewPassw0rd!").Returns("new-hash");
    }

    [Fact]
    public async Task Handle_OwnPasswordWithCorrectCurrent_Succeeds()
    {
        User me = _fx.AddUser("me@example.com");

        Result<ChangePasswordResponse> result = await _handler.Handle(
            new ChangePasswordCommand(me.Id, "current", "NewPassw0rd!", new Caller(me.Id, false)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-hash", me.PasswordHash);
    }

    [Fact]
    public async Task Handle_OwnPasswordWithoutCurrent_IsRejected()
    {
        User me = _fx.AddUser("me@example.com");

        Result<ChangePasswordResponse> result = await _handler.Handle(
            new ChangePasswordCommand(me.Id, null, "NewPassw0rd!", new Caller(me.Id, false)), CancellationToken.None);

        Assert.Equal("User.CurrentPasswordRequired", result.Error.Code);
        Assert.Equal("hash", me.PasswordHash);
    }

    [Fact]
    public async Task Handle_OwnPasswordWithWrongCurrent_IsRejected()
    {
        User me = _fx.AddUser("me@example.com");

        Result<ChangePasswordResponse> result = await _handler.Handle(
            new ChangePasswordCommand(me.Id, "wrong", "NewPassw0rd!", new Caller(me.Id, false)), CancellationToken.None);

        Assert.Equal("User.InvalidPassword", result.Error.Code);
    }

    [Fact]
    public async Task Handle_AdminResetsAnotherUsersPassword_NeedsNoCurrentPassword()
    {
        User admin = _fx.AddUser("admin@example.com", isAdmin: true);
        User other = _fx.AddUser("other@example.com");

        Result<ChangePasswordResponse> result = await _handler.Handle(
            new ChangePasswordCommand(other.Id, null, "NewPassw0rd!", new Caller(admin.Id, true)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-hash", other.PasswordHash);
    }

    [Fact]
    public async Task Handle_AdminChangingOwnPassword_StillNeedsCurrentPassword()
    {
        User admin = _fx.AddUser("admin@example.com", isAdmin: true);

        Result<ChangePasswordResponse> result = await _handler.Handle(
            new ChangePasswordCommand(admin.Id, null, "NewPassw0rd!", new Caller(admin.Id, true)), CancellationToken.None);

        Assert.Equal("User.CurrentPasswordRequired", result.Error.Code);
    }

    [Fact]
    public async Task Handle_OtherAccountAsNonAdmin_ReturnsNotFound()
    {
        User me = _fx.AddUser("me@example.com");
        User other = _fx.AddUser("other@example.com");

        Result<ChangePasswordResponse> result = await _handler.Handle(
            new ChangePasswordCommand(other.Id, "current", "NewPassw0rd!", new Caller(me.Id, false)), CancellationToken.None);

        Assert.Equal("User.NotFound", result.Error.Code);
        Assert.Equal("hash", other.PasswordHash);
    }

    [Fact]
    public async Task Handle_InactiveAccount_IsRejected()
    {
        User admin = _fx.AddUser("admin@example.com", isAdmin: true);
        User inactive = _fx.AddUser("off@example.com", isActive: false);

        Result<ChangePasswordResponse> result = await _handler.Handle(
            new ChangePasswordCommand(inactive.Id, null, "NewPassw0rd!", new Caller(admin.Id, true)), CancellationToken.None);

        Assert.Equal("User.Inactive", result.Error.Code);
    }
}
