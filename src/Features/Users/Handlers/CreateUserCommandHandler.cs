using Application.CQRS;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Domain.Common.ValueObjects;
using Features.Users.Commands;
using Infrastructure.Authentication;
using Microsoft.Extensions.Logging;
using Features.Users.Logging;
using Infrastructure.Logging;

namespace Features.Users.Handlers;

public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, Result<CreateUserResponse>>
{
    private readonly IUserEfRepository<User> _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<CreateUserCommandHandler> _logger;

    public CreateUserCommandHandler(IUserEfRepository<User> userRepository, IPasswordHasher passwordHasher, ILogger<CreateUserCommandHandler> logger)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<Result<CreateUserResponse>> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        // Check if email already exists
        IEnumerable<User?> existingUsers = await _userRepository.FindAsync(
            u => u.Email.Value == command.Email,
            cancellationToken);

        if (existingUsers.Any())
        {
            // Log failure with reason code and numeric id
            LogMessages.UserCreateFailed(_logger, command.Email, FailureReasons.EmailAlreadyExists, FailureReasons.EmailAlreadyExistsCode);

            return Result.Failure<CreateUserResponse>(
                Error.Conflict("User.EmailExists", "A user with this email already exists"));
        }

        // Create email value object using factory pattern
        Result<EmailAddress> emailResult = EmailAddress.Create(command.Email);
        if (emailResult.IsFailure)
        {
            return Result.Failure<CreateUserResponse>(emailResult.Error);
        }

        EmailAddress email = emailResult.Value;

        // Hash the password using BCrypt
        string passwordHash = _passwordHasher.HashPassword(command.Password);

        // Create new user with IsActive set to true
        var user = new User(
            Guid.NewGuid(),
            command.FirstName,
            command.LastName,
            passwordHash,
            email,
            isActive: true
        );

        _userRepository.Insert(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

        var response = new CreateUserResponse(
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
