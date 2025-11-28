using System.Security.Claims;
using Domain.Common.Entities;

namespace Infrastructure.Authentication;

public interface IJwtTokenService
{
    string GenerateToken(User user, IEnumerable<Role> roles);
    string GenerateRefreshToken();
    ClaimsPrincipal? ValidateToken(string token);
}
