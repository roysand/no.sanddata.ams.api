using System.Linq.Expressions;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Domain.Common.ValueObjects;
using Features.Users.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Features.Tests.Users;

public class AdminBootstrapServiceTests
{
    private const string OwnerEmail = "owner@example.com";

    private readonly Role _adminRole = new(Guid.NewGuid(), RoleNames.Admin, "Admin", true);
    private readonly Role _userRole = new(Guid.NewGuid(), RoleNames.User, "User", true);
    private readonly User _owner = new(Guid.NewGuid(), "Owner", "Person", "hash", EmailAddress.Create(OwnerEmail).Value, true);
    private readonly List<UserRole> _rows = [];

    private readonly IUserRepository<User> _users = Substitute.For<IUserRepository<User>>();
    private readonly IRoleRepository<Role> _roles = Substitute.For<IRoleRepository<Role>>();
    private readonly IUserRoleRepository<UserRole> _userRoles = Substitute.For<IUserRoleRepository<UserRole>>();

    public AdminBootstrapServiceTests()
    {
        _roles.AllAsync(Arg.Any<CancellationToken>()).Returns([_adminRole, _userRole]);
        _users.FindAsync(Arg.Any<Expression<Func<User, bool>>>(), Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(call => ((IEnumerable<User?>)[_owner]).Where(u => call.Arg<Expression<Func<User, bool>>>().Compile()(u!)).ToList());
        _userRoles.ExistsAsync(Arg.Any<Expression<Func<UserRole, bool>>>(), Arg.Any<CancellationToken>())
            .Returns(call => _rows.Any(call.Arg<Expression<Func<UserRole, bool>>>().Compile()));
        _userRoles.Insert(Arg.Do<UserRole>(_rows.Add));
    }

    private async Task RunAsync(string? ownerEmail = OwnerEmail)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_users);
        services.AddSingleton(_roles);
        services.AddSingleton(_userRoles);
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Bootstrap:OwnerEmail"] = ownerEmail })
            .Build();

        var service = new AdminBootstrapService(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            configuration, NullLogger<AdminBootstrapService>.Instance);

        await service.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Start_NoAdminExists_PromotesOwnerAndGivesUserRole()
    {
        await RunAsync();

        Assert.Contains(_rows, r => r.UserId == _owner.Id && r.RoleId == _adminRole.Id);
        Assert.Contains(_rows, r => r.UserId == _owner.Id && r.RoleId == _userRole.Id);
        await _userRoles.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_OwnerAlreadyHasUserRole_DoesNotAddItTwice()
    {
        _rows.Add(new UserRole(_owner.Id, _userRole.Id, DateTime.UtcNow));

        await RunAsync();

        Assert.Single(_rows, r => r.RoleId == _userRole.Id);
        Assert.Single(_rows, r => r.RoleId == _adminRole.Id);
    }

    [Fact]
    public async Task Start_AnAdminAlreadyExists_ChangesNothing()
    {
        _rows.Add(new UserRole(Guid.NewGuid(), _adminRole.Id, DateTime.UtcNow));

        await RunAsync();

        Assert.Single(_rows);
        await _userRoles.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Start_OwnerAccountDoesNotExist_ChangesNothing()
    {
        await RunAsync("someone.else@example.com");

        Assert.Empty(_rows);
    }

    [Fact]
    public async Task Start_NoOwnerEmailConfigured_ChangesNothing()
    {
        await RunAsync(ownerEmail: null);

        Assert.Empty(_rows);
    }

    [Fact]
    public async Task Start_RolesNotSeededYet_ChangesNothing()
    {
        _roles.AllAsync(Arg.Any<CancellationToken>()).Returns([]);

        await RunAsync();

        Assert.Empty(_rows);
    }

    [Fact]
    public async Task Start_RunTwice_IsIdempotent()
    {
        await RunAsync();
        await RunAsync();

        Assert.Equal(2, _rows.Count);
    }

    [Fact]
    public async Task Start_RepositoryThrows_DoesNotFailStartup()
    {
        _roles.AllAsync(Arg.Any<CancellationToken>()).Returns<IEnumerable<Role?>>(_ => throw new InvalidOperationException("db down"));

        await RunAsync(); // must not throw
    }
}
