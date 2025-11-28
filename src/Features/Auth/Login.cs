using Application.Common;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using FastEndpoints;
using FluentValidation;
using Infrastructure.Authentication;
using MediatR;

namespace Features.Auth;

public static class Login
{
    public record LoginRequest(string Email, string Password) : IRequest<Result<LoginResponse>>;

    public record LoginResponse(
        string AccessToken,
        string RefreshToken,
        string Email,
        string[] Roles,
        DateTime AccessTokenExpiry,
        DateTime RefreshTokenExpiry
    );

    internal sealed class Handler : IRequestHandler<LoginRequest, Result<LoginResponse>>
    {
        private readonly IUserEfRepository<User> _userRepository;
        private readonly IRefreshTokenRepository _refreshTokenRepository;
        private readonly IJwtTokenService _jwtTokenService;

        public Handler(
            IUserEfRepository<User> userRepository,
            IRefreshTokenRepository refreshTokenRepository,
            IJwtTokenService jwtTokenService)
        {
            _userRepository = userRepository;
            _refreshTokenRepository = refreshTokenRepository;
            _jwtTokenService = jwtTokenService;
        }

        public async Task<Result<LoginResponse>> Handle(
            LoginRequest request,
            CancellationToken cancellationToken)
        {
            var users = await _userRepository.FindAsync(
                u => u.Email.Value == request.Email && u.IsActive,
                cancellationToken);

            var user = users.FirstOrDefault();
            if (user == null)
            {
                return Result.Failure<LoginResponse>(
                    Error.NotFound("Auth.InvalidCredentials", "Invalid email or password"));
            }

            // TODO: Implement password verification using BCrypt or similar
            // For now, this is a placeholder - you should hash passwords and verify them properly
            // Example: if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            //     return Result.Failure<LoginResponse>(Error.NotFound("Auth.InvalidCredentials", "Invalid email or password"));

            // Generate tokens
            var accessToken = _jwtTokenService.GenerateToken(user, user.Roles);
            var refreshToken = _jwtTokenService.GenerateRefreshToken();

            // Store refresh token in database
            var accessTokenExpiry = DateTime.UtcNow.AddHours(6);
            var refreshTokenExpiry = DateTime.UtcNow.AddDays(14);

            var refreshTokenEntity = new Domain.Common.Entities.RefreshToken
            {
                UserId = user.Id,
                Token = refreshToken,
                ExpiresAt = refreshTokenExpiry,
                CreatedAt = DateTime.UtcNow
            };

            _refreshTokenRepository.Insert(refreshTokenEntity);
            await _refreshTokenRepository.SaveChangesAsync(cancellationToken);

            var roles = user.Roles.Select(r => r.Name).ToArray();

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

    public class LoginValidator : Validator<LoginRequest>
    {
        public LoginValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required")
                .EmailAddress().WithMessage("A valid email is required");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required")
                .MinimumLength(6).WithMessage("Password must be at least 6 characters");
        }
    }
}

public class LoginEndpoint : Endpoint<Login.LoginRequest, Login.LoginResponse>
{
    private readonly ISender _sender;

    public LoginEndpoint(ISender sender) => _sender = sender;

    public override void Configure()
    {
        Post("/api/auth/login");
        AllowAnonymous();
        Summary(s =>
        {
            s.Summary = "User login";
            s.Description = "Authenticate user and receive JWT token";
            s.ExampleRequest = new Login.LoginRequest("user@example.com", "password123");
        });
    }

    public override async Task<Login.LoginResponse> HandleAsync(
        Login.LoginRequest req,
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
