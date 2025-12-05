using Domain.Common;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Domain.Common.ValueObjects;
using FastEndpoints;
using FluentValidation;
using Infrastructure.Authentication;
using MediatR;

namespace Features.Users;

public static class CreateUser
{
    public record CreateUserRequest(
        string FirstName,
        string LastName,
        string Email,
        string Password
    ) : IRequest<Result<UserResponse>>;

    public record UserResponse(
        Guid Id,
        string FirstName,
        string LastName,
        string Email,
        bool IsActive,
        string[] Roles,
        string[] Locations
    );

    internal sealed class Handler : IRequestHandler<CreateUserRequest, Result<UserResponse>>
    {
        private readonly IUserEfRepository<User> _userRepository;
        private readonly IPasswordHasher _passwordHasher;

        public Handler(IUserEfRepository<User> userRepository, IPasswordHasher passwordHasher)
        {
            _userRepository = userRepository;
            _passwordHasher = passwordHasher;
        }

        public async Task<Result<UserResponse>> Handle(
            CreateUserRequest request,
            CancellationToken cancellationToken)
        {
            // Check if email already exists
            IEnumerable<User?> existingUsers = await _userRepository.FindAsync(
                u => u.Email.Value == request.Email,
                cancellationToken);

            if (existingUsers.Any())
            {
                return Result.Failure<UserResponse>(
                    Error.Conflict("User.EmailExists", "A user with this email already exists"));
            }

            // Create email value object using factory pattern
            Result<EmailAddress> emailResult = EmailAddress.Create(request.Email);
            if (emailResult.IsFailure)
            {
                return Result.Failure<UserResponse>(emailResult.Error);
            }

            EmailAddress email = emailResult.Value;

            // Hash the password using BCrypt
            string passwordHash = _passwordHasher.HashPassword(request.Password);

            // Create new user with IsActive set to true
            var user = new User(
                Guid.NewGuid(),
                request.FirstName,
                request.LastName,
                passwordHash,
                email,
                isActive: true
            );

            _userRepository.Insert(user);
            await _userRepository.SaveChangesAsync(cancellationToken);

            var response = new UserResponse(
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email.Value,
                user.IsActive,
                user.Roles.Select(r => r.Name).ToArray(),
                user.Locations.Select(l => l.Name).ToArray()
            );

            return Result.Success(response);
        }
    }

    public class CreateUserValidator : Validator<CreateUserRequest>
    {
        public CreateUserValidator()
        {
            RuleFor(x => x.FirstName)
                .NotEmpty().WithMessage("First name is required")
                .MaximumLength(100).WithMessage("First name must not exceed 100 characters");

            RuleFor(x => x.LastName)
                .NotEmpty().WithMessage("Last name is required")
                .MaximumLength(100).WithMessage("Last name must not exceed 100 characters");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required")
                .EmailAddress().WithMessage("A valid email is required")
                .MaximumLength(255).WithMessage("Email must not exceed 255 characters");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required")
                .MinimumLength(8).WithMessage("Password must be at least 8 characters")
                .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter")
                .Matches(@"[a-z]").WithMessage("Password must contain at least one lowercase letter")
                .Matches(@"[0-9]").WithMessage("Password must contain at least one number")
                .Matches(@"[\W_]").WithMessage("Password must contain at least one special character");
        }
    }
}

public class CreateUserEndpoint : Endpoint<CreateUser.CreateUserRequest, CreateUser.UserResponse>
{
    private readonly ISender _sender;

    public CreateUserEndpoint(ISender sender) => _sender = sender;

    public override void Configure()
    {
        Post("/api/users");
        AllowAnonymous(); // Change to require authentication/authorization as needed
        Summary(s =>
        {
            s.Summary = "Create a new user";
            s.Description = "Register a new user in the system. The user will be created with IsActive set to true.";
            s.ExampleRequest = new CreateUser.CreateUserRequest(
                "John",
                "Doe",
                "john.doe@example.com",
                "SecurePass123!"
            );
            s.Response(201, "User created successfully");
            s.Response(409, "User with this email already exists");
            s.Response(400, "Invalid request data");
        });
    }

    public override async Task<CreateUser.UserResponse> HandleAsync(
        CreateUser.CreateUserRequest req,
        CancellationToken ct)
    {
        Result<CreateUser.UserResponse> result = await _sender.Send(req, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors(result.Error.Type switch
            {
                ErrorType.Conflict => 409,
                ErrorType.Validation => 400,
                _ => 400
            });
        }

        // Set 201 Created status and Location header
        HttpContext.Response.StatusCode = 201;
        HttpContext.Response.Headers.Location = $"/api/users/{result.Value.Id}";

        return result.Value;
    }
}
