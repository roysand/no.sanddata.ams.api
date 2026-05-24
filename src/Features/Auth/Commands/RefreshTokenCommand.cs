using Application.CQRS;
using Domain.Common;

namespace Features.Auth.Commands;

public record RefreshTokenCommand(string RefreshToken) : ICommand<Result<RefreshTokenResponse>>;

public record RefreshTokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiry,
    DateTime RefreshTokenExpiry
);
