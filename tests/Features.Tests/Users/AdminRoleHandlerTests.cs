using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Commands;
using Features.Users.Handlers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Features.Tests.Users;

public class AdminRoleHandlerTests
{
    private readonly UserFixture _fx = new();
    private readonly GrantAdminCommandHandler _grant;
    private readonly RevokeAdminCommandHandler _revoke;

    public AdminRoleHandlerTests()
    {
        _grant = new GrantAdminCommandHandler(_fx.UserRepository, _fx.RoleRepository, _fx.UserRoleRepository,
            Substitute.For<ILogger<GrantAdminCommandHandler>>());
        _revoke = new RevokeAdminCommandHandler(_fx.UserRepository, _fx.RoleRepository, _fx.UserRoleRepository,
            Substitute.For<ILogger<RevokeAdminCommandHandler>>());
        _fx.UserRepository.ExistsAsync(Arg.Any<System.Linq.Expressions.Expression<Func<User, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(_fx.Users.Any(call.Arg<System.Linq.Expressions.Expression<Func<User, bool>>>().Compile())));
    }

    [Fact]
    public async Task Grant_OrdinaryUser_BecomesAdminAndKeepsUserRole()
    {
        User admin = _fx.AddUser("admin@example.com", isAdmin: true);
        User other = _fx.AddUser("other@example.com");

        Result<AdminRoleChangeResponse> result =
            await _grant.Handle(new GrantAdminCommand(other.Id, new Caller(admin.Id, true)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Changed);
        Assert.True(_fx.IsAdmin(other));
        Assert.Contains(_fx.UserRoles, ur => ur.UserId == other.Id && ur.RoleId == _fx.UserRole.Id);
    }

    [Fact]
    public async Task Grant_AlreadyAdmin_IsIdempotent()
    {
        User admin = _fx.AddUser("admin@example.com", isAdmin: true);
        int rows = _fx.UserRoles.Count;

        Result<AdminRoleChangeResponse> result =
            await _grant.Handle(new GrantAdminCommand(admin.Id, new Caller(admin.Id, true)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Changed);
        Assert.Equal(rows, _fx.UserRoles.Count);
    }

    [Fact]
    public async Task Grant_UnknownUser_ReturnsNotFound()
    {
        User admin = _fx.AddUser("admin@example.com", isAdmin: true);

        Result<AdminRoleChangeResponse> result =
            await _grant.Handle(new GrantAdminCommand(Guid.NewGuid(), new Caller(admin.Id, true)), CancellationToken.None);

        Assert.Equal("User.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Revoke_WhenAnotherAdminExists_RemovesAdminButKeepsUserRole()
    {
        User first = _fx.AddUser("a1@example.com", isAdmin: true);
        User second = _fx.AddUser("a2@example.com", isAdmin: true);

        Result<AdminRoleChangeResponse> result =
            await _revoke.Handle(new RevokeAdminCommand(second.Id, new Caller(first.Id, true)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Changed);
        Assert.False(_fx.IsAdmin(second));
        Assert.Contains(_fx.UserRoles, ur => ur.UserId == second.Id && ur.RoleId == _fx.UserRole.Id);
    }

    [Fact]
    public async Task Revoke_TheLastActiveAdmin_ReturnsLastAdminConflict()
    {
        User only = _fx.AddUser("only@example.com", isAdmin: true);
        _fx.AddUser("other@example.com");

        Result<AdminRoleChangeResponse> result =
            await _revoke.Handle(new RevokeAdminCommand(only.Id, new Caller(only.Id, true)), CancellationToken.None);

        Assert.Equal("User.LastAdmin", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.True(_fx.IsAdmin(only));
    }

    [Fact]
    public async Task Revoke_FromNonAdmin_SucceedsWithoutChange()
    {
        User admin = _fx.AddUser("admin@example.com", isAdmin: true);
        User other = _fx.AddUser("other@example.com");

        Result<AdminRoleChangeResponse> result =
            await _revoke.Handle(new RevokeAdminCommand(other.Id, new Caller(admin.Id, true)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Changed);
    }

    [Fact]
    public async Task Revoke_UnknownUser_ReturnsNotFound()
    {
        User admin = _fx.AddUser("admin@example.com", isAdmin: true);

        Result<AdminRoleChangeResponse> result =
            await _revoke.Handle(new RevokeAdminCommand(Guid.NewGuid(), new Caller(admin.Id, true)), CancellationToken.None);

        Assert.Equal("User.NotFound", result.Error.Code);
    }

}
