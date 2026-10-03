using System.Linq.Expressions;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Domain.Common.ValueObjects;
using NSubstitute;

namespace Features.Tests.Users;

/// <summary>In-memory users/roles/user-roles behind NSubstitute repositories, evaluating predicates for real.</summary>
internal sealed class UserFixture
{
    public Role AdminRole { get; } = new(Guid.NewGuid(), RoleNames.Admin, "Admin", true);
    public Role UserRole { get; } = new(Guid.NewGuid(), RoleNames.User, "User", true);
    public List<User> Users { get; } = [];
    public List<UserRole> UserRoles { get; } = [];
    public List<User> Deleted { get; } = [];

    public IUserRepository<User> UserRepository { get; } = Substitute.For<IUserRepository<User>>();
    public IRoleRepository<Role> RoleRepository { get; } = Substitute.For<IRoleRepository<Role>>();
    public IUserRoleRepository<UserRole> UserRoleRepository { get; } = Substitute.For<IUserRoleRepository<UserRole>>();

    public UserFixture()
    {
        UserRepository.FindAsync(Arg.Any<Expression<Func<User, bool>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(call => Task.FromResult<IEnumerable<User?>>(
                Users.Where(call.Arg<Expression<Func<User, bool>>>().Compile()).Cast<User?>().ToList()));
        UserRepository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(Users.FirstOrDefault(u => u.Id == call.Arg<Guid>())));
        UserRepository.Delete(Arg.Do<User>(Deleted.Add));

        RoleRepository.FindAsync(Arg.Any<Expression<Func<Role, bool>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(call => Task.FromResult<IEnumerable<Role?>>(
                new[] { AdminRole, UserRole }.Where(call.Arg<Expression<Func<Role, bool>>>().Compile()).Cast<Role?>().ToList()));
        RoleRepository.AllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<Role?>>([AdminRole, UserRole]));

        UserRoleRepository.FindAsync(Arg.Any<Expression<Func<UserRole, bool>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(call => Task.FromResult<IEnumerable<UserRole?>>(
                UserRoles.Where(call.Arg<Expression<Func<UserRole, bool>>>().Compile()).Cast<UserRole?>().ToList()));
        UserRoleRepository.ExistsAsync(Arg.Any<Expression<Func<UserRole, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(UserRoles.Any(call.Arg<Expression<Func<UserRole, bool>>>().Compile())));
        UserRoleRepository.Insert(Arg.Do<UserRole>(UserRoles.Add));
        UserRoleRepository.Delete(Arg.Do<UserRole>(ur => UserRoles.Remove(ur)));
    }

    public User AddUser(string email, bool isActive = true, bool isAdmin = false)
    {
        var user = new User(Guid.NewGuid(), "First", "Last", "hash", EmailAddress.Create(email).Value, isActive);
        Users.Add(user);
        UserRoles.Add(new UserRole(user.Id, UserRole.Id, DateTime.UtcNow));
        if (isAdmin)
        {
            UserRoles.Add(new UserRole(user.Id, AdminRole.Id, DateTime.UtcNow));
        }

        return user;
    }

    public bool IsAdmin(User user) => UserRoles.Any(ur => ur.UserId == user.Id && ur.RoleId == AdminRole.Id);
}
