using Application.CQRS;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Auth.Commands;
using Infrastructure.Authentication;
using Microsoft.Extensions.Logging;
using Infrastructure.Logging;
using Features.Auth.Logging;

namespace Features.Auth.Handlers;

public class LoginCommandHandler : ICommandHandler<LoginCommand, Result<LoginResponse>>
{
    private readonly IUserEfRepository<User> _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        IUserEfRepository<User> userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IJwtTokenService jwtTokenService,
        IPasswordHasher passwordHasher,
        ILogger<LoginCommandHandler> logger)
    {
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _jwtTokenService = jwtTokenService;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<Result<LoginResponse>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        IEnumerable<User?> users = await _userRepository.FindAsync(
            u => u.Email.Value == command.Email && u.IsActive,
            cancellationToken);

        User? user = users.FirstOrDefault();
        if (user is null)
        {
            // Log failure with reason code and numeric id
            LogMessages.LoginFailed(_logger, command.Email, Infrastructure.Logging.FailureReasons.NoSuchUser, Infrastructure.Logging.FailureReasons.NoSuchUserCode);

            return Result.Failure<LoginResponse>(
                Error.NotFound("Auth.InvalidCredentials", "Invalid email or password"));
        }

        // Verify password using BCrypt
        if (!_passwordHasher.VerifyPassword(command.Password, user.PasswordHash))
        {
            // Log failure with reason code and numeric id
            LogMessages.LoginFailed(_logger, command.Email, Infrastructure.Logging.FailureReasons.BadCredentials, Infrastructure.Logging.FailureReasons.BadCredentialsCode);

            return Result.Failure<LoginResponse>(
                Error.NotFound("Auth.InvalidCredentials", "Invalid email or password"));
        }

        // Generate tokens
        string accessToken = _jwtTokenService.GenerateToken(user, user.Roles);
        string refreshToken = _jwtTokenService.GenerateRefreshToken();

        // Store refresh token in database
        DateTime accessTokenExpiry = DateTime.UtcNow.AddHours(6);
        DateTime refreshTokenExpiry = DateTime.UtcNow.AddDays(14);

        var refreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            Token = refreshToken,
            ExpiresAt = refreshTokenExpiry,
            CreatedAt = DateTime.UtcNow
        };

        _refreshTokenRepository.Insert(refreshTokenEntity);
        await _refreshTokenRepository.SaveChangesAsync(cancellationToken);

        string[] roles = user.Roles.Select(r => r.Name).ToArray();

        return Result.Success(new LoginResponse(
            accessToken,
            refreshToken,
            user.Email.Value,
            roles,
            accessTokenExpiry,
            refreshTokenExpiry
        ));
    }
}
