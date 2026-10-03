using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Domain.Common;
using Domain.Common.Entities;
using Domain.Common.ValueObjects;
using Infrastructure.Authentication;
using Microsoft.Extensions.Configuration;

namespace Features.Tests.Users;

public class JwtTokenServiceTests
{
    private readonly JwtTokenService _service;
    private readonly User _user = new(Guid.NewGuid(), "Test", "User", "hash", EmailAddress.Create("t@example.com").Value, true);

    public JwtTokenServiceTests()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = "unit-test-secret-key-that-is-at-least-32-characters-long",
                ["JwtSettings:Issuer"] = "AmsApi",
                ["JwtSettings:Audience"] = "AmsClient"
            })
            .Build();
        _service = new JwtTokenService(configuration);
    }

    private static Role RoleNamed(string name) => new(Guid.NewGuid(), name, name, true);

    [Fact]
    public void GenerateToken_UserWithRoles_ContainsOneRoleClaimPerRole()
    {
        string token = _service.GenerateToken(_user, [RoleNamed(RoleNames.Admin), RoleNamed(RoleNames.User)]);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        string[] roles = jwt.Claims.Where(c => c.Type is "role" or ClaimTypes.Role).Select(c => c.Value).ToArray();
        Assert.Equal([RoleNames.Admin, RoleNames.User], roles.OrderBy(r => r));
    }

    [Fact]
    public void ValidateToken_AdminToken_PrincipalIsInAdminRoleOnly()
    {
        string token = _service.GenerateToken(_user, [RoleNamed(RoleNames.Admin), RoleNamed(RoleNames.User)]);

        ClaimsPrincipal? principal = _service.ValidateToken(token);

        Assert.NotNull(principal);
        Assert.True(principal.IsInRole(RoleNames.Admin));
        Assert.True(principal.IsInRole(RoleNames.User));
        Assert.False(principal.IsInRole("Manager"));
    }

    [Fact]
    public void ValidateToken_UserWithoutRoles_IsNotAdmin()
    {
        string token = _service.GenerateToken(_user, []);

        ClaimsPrincipal? principal = _service.ValidateToken(token);

        Assert.NotNull(principal);
        Assert.False(principal.IsInRole(RoleNames.Admin));
    }
}
