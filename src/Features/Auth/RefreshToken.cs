using Application.Common;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using FastEndpoints;
using FluentValidation;
using Infrastructure.Authentication;
using MediatR;

namespace Features.Auth;

public static class RefreshToken
{
    public record RefreshTokenRequest(string RefreshToken) : IRequest<Result<RefreshTokenResponse>>;

    public record RefreshTokenResponse(
        string AccessToken,
        string RefreshToken,
        DateTime AccessTokenExpiry,
        DateTime RefreshTokenExpiry
    );

    internal sealed class Handler : IRequestHandler<RefreshTokenRequest, Result<RefreshTokenResponse>>
    {
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IUserEfRepository<User> _userRepository;
        private readonly IJwtTokenService _jwtTokenService;

        public Handler(
            IRefreshTokenRepository refreshTokenRepository,
            IUserEfRepository<User> userRepository,
            IJwtTokenService jwtTokenService)
        {
            _refreshTokenRepository = refreshTokenRepository;
            _userRepository = userRepository;
            _jwtTokenService = jwtTokenService;
        }

        public async Task<Result<RefreshTokenResponse>> Handle(
            RefreshTokenRequest request,
            CancellationToken cancellationToken)
        {
            // Find refresh token
            var tokens = await _refreshTokenRepository.FindAsync(
                rt => rt.Token == request.RefreshToken,
                cancellationToken);

            var refreshToken = tokens.FirstOrDefault();

            if (refreshToken == null || !refreshToken.IsActive)
            {
                return Result.Failure<RefreshTokenResponse>(
                    Error.NotFound("Auth.InvalidRefreshToken", "Invalid or expired refresh token"));
            }

            // Get user
            var user = await _userRepository.GetAsync(refreshToken.UserId, cancellationToken);
            if (user == null || !user.IsActive)
            {
                return Result.Failure<RefreshTokenResponse>(
                    Error.NotFound("Auth.UserNotFound", "User not found or inactive"));
            }

            // Generate new tokens
            var newAccessToken = _jwtTokenService.GenerateToken(user, user.Roles);
            var newRefreshToken = _jwtTokenService.GenerateRefreshToken();

            var accessTokenExpiry = DateTime.UtcNow.AddHours(6);
            var refreshTokenExpiry = DateTime.UtcNow.AddDays(14);

            // Revoke old refresh token (token rotation)
            refreshToken.RevokedAt = DateTime.UtcNow;
            refreshToken.ReplacedByToken = newRefreshToken;
            refreshToken.ReasonRevoked = "Replaced by new token";
            _refreshTokenRepository.Update(refreshToken);

            // Create new refresh token
            var newRefreshTokenEntity = new Domain.Common.Entities.RefreshToken
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

    public class RefreshTokenValidator : Validator<RefreshTokenRequest>
    {
        public RefreshTokenValidator()
        {
            RuleFor(x => x.RefreshToken)
                .NotEmpty().WithMessage("Refresh token is required");
        }
    }
}

public class RefreshTokenEndpoint : Endpoint<RefreshToken.RefreshTokenRequest, RefreshToken.RefreshTokenResponse>
{
    private readonly ISender _sender;

    public RefreshTokenEndpoint(ISender sender) => _sender = sender;

    public override void Configure()
    {
        Post("/api/auth/refresh");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "Refresh access token";
            s.Description = "Get new access token using refresh token";
            s.ExampleRequest = new RefreshToken.RefreshTokenRequest("your-refresh-token-here");
        });
    }

    public override async Task<RefreshToken.RefreshTokenResponse> HandleAsync(
        RefreshToken.RefreshTokenRequest req,
        CancellationToken ct)
    {
        var result = await _sender.Send(req, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors();
        }

        return result.Value;
    }
}
