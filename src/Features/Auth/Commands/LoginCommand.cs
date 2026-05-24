using Application.CQRS;
using Domain.Common;

namespace Features.Auth.Commands;

public record LoginCommand(string Email, string Password) : ICommand<Result<LoginResponse>>;

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    string Email,
    string[] Roles,
    DateTime AccessTokenExpiry,
    DateTime RefreshTokenExpiry
);
