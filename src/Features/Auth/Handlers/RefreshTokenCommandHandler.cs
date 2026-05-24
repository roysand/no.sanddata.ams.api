using Application.CQRS;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Auth.Commands;
using Infrastructure.Authentication;

namespace Features.Auth.Handlers;

public class RefreshTokenCommandHandler : ICommandHandler<RefreshTokenCommand, Result<RefreshTokenResponse>>
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUserEfRepository<User> _userRepository;
    private readonly IJwtTokenService _jwtTokenService;

    public RefreshTokenCommandHandler(
        IRefreshTokenRepository refreshTokenRepository,
        IUserEfRepository<User> userRepository,
        IJwtTokenService jwtTokenService)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _userRepository = userRepository;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<Result<RefreshTokenResponse>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        // Find refresh token
        IEnumerable<RefreshToken?> tokens = await _refreshTokenRepository.FindAsync(
            rt => rt.Token == command.RefreshToken,
            cancellationToken);

        RefreshToken? refreshToken = tokens.FirstOrDefault();

        if (refreshToken is null || !refreshToken.IsActive)
        {
            return Result.Failure<RefreshTokenResponse>(
                Error.NotFound("Auth.InvalidRefreshToken", "Invalid or expired refresh token"));
        }

        // Get user
        User? user = await _userRepository.GetByIdAsync(refreshToken.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Result.Failure<RefreshTokenResponse>(
                Error.NotFound("Auth.UserNotFound", "User not found or inactive"));
        }

        // Generate new tokens
        string newAccessToken = _jwtTokenService.GenerateToken(user, user.Roles);
        string newRefreshToken = _jwtTokenService.GenerateRefreshToken();

        DateTime accessTokenExpiry = DateTime.UtcNow.AddHours(6);
        DateTime refreshTokenExpiry = DateTime.UtcNow.AddDays(14);

        // Revoke old refresh token (token rotation)
        refreshToken.RevokedAt = DateTime.UtcNow;
        refreshToken.ReplacedByToken = newRefreshToken;
        refreshToken.ReasonRevoked = "Replaced by new token";
        _refreshTokenRepository.Update(refreshToken);

        // Create new refresh token
        var newRefreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            Token = newRefreshToken,
            ExpiresAt = refreshTokenExpiry,
            CreatedAt = DateTime.UtcNow
        };
        _refreshTokenRepository.Insert(newRefreshTokenEntity);
        await _refreshTokenRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(new RefreshTokenResponse(
            newAccessToken,
            newRefreshToken,
            accessTokenExpiry,
            refreshTokenExpiry
        ));
    }
}
