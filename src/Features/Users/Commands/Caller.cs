using System.Security.Claims;
using Domain.Common;

namespace Features.Users.Commands;

/// <summary>The signed-in user making a request, as read from the token.</summary>
public record Caller(Guid Id, bool IsAdmin)
{
    public static Caller From(ClaimsPrincipal user) =>
        new(Guid.Parse(user.FindFirst(ClaimTypes.NameIdentifier)!.Value), user.IsInRole(RoleNames.Admin));
}
