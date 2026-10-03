using Domain.Common;
using Features.Users.Commands;
using Features.Users.Handlers;
using Infrastructure.Authentication;
using NSubstitute;

namespace Features.Tests.Users;

public class CreateUserCommandHandlerTests
{
    private readonly UserFixture _fx = new();
    private readonly CreateUserCommandHandler _handler;

    public CreateUserCommandHandlerTests()
    {
        IPasswordHasher hasher = Substitute.For<IPasswordHasher>();
        hasher.HashPassword(Arg.Any<string>()).Returns("hashed");
        _handler = new CreateUserCommandHandler(_fx.UserRepository, hasher, _fx.RoleRepository, _fx.UserRoleRepository);
    }

    [Fact]
    public async Task Handle_NewUser_GetsExactlyTheUserRole()
    {
        Guid? createdId = null;
        _fx.UserRepository.Insert(Arg.Do<Domain.Common.Entities.User>(u => createdId = u.Id));

        Result<CreateUserResponse> result = await _handler.Handle(
            new CreateUserCommand("New", "Person", "new@example.com", "Passw0rd!x"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([RoleNames.User], result.Value.Roles);
        Assert.NotNull(createdId);
        Assert.Single(_fx.UserRoles, ur => ur.UserId == createdId);
        Assert.Contains(_fx.UserRoles, ur => ur.UserId == createdId && ur.RoleId == _fx.UserRole.Id);
        await _fx.UserRepository.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateEmail_ReturnsEmailExistsAndAssignsNoRole()
    {
        _fx.AddUser("taken@example.com");
        int rows = _fx.UserRoles.Count;

        Result<CreateUserResponse> result = await _handler.Handle(
            new CreateUserCommand("New", "Person", "taken@example.com", "Passw0rd!x"), CancellationToken.None);

        Assert.Equal("User.EmailExists", result.Error.Code);
        Assert.Equal(rows, _fx.UserRoles.Count);
    }
}
