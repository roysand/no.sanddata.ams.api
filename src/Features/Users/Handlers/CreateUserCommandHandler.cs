using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Domain.Common.ValueObjects;
using Features.Users.Commands;
using Infrastructure.Authentication;

namespace Features.Users.Handlers;

public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, Result<CreateUserResponse>>
{
    private readonly IUserRepository<User> _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IRoleRepository<Role> _roleRepository;
    private readonly IUserRoleRepository<UserRole> _userRoleRepository;

    public CreateUserCommandHandler(
        IUserRepository<User> userRepository,
        IPasswordHasher passwordHasher,
        IRoleRepository<Role> roleRepository,
        IUserRoleRepository<UserRole> userRoleRepository)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _roleRepository = roleRepository;
        _userRoleRepository = userRoleRepository;
    }

    public async Task<Result<CreateUserResponse>> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        // Check if email already exists
        IEnumerable<User?> existingUsers = await _userRepository.FindAsync(
            u => u.Email.Value == command.Email,
            cancellationToken);

        if (existingUsers.Any())
        {
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

        // New users are ordinary users; promoting one to Admin is a separate, explicit admin action.
        Role? userRole = (await _roleRepository.FindAsync(r => r.Name == RoleNames.User, cancellationToken))
            .FirstOrDefault();
        if (userRole is null)
        {
            return Result.Failure<CreateUserResponse>(
                Error.Problem("Role.UserRoleMissing", "The User role does not exist; has the database migration been applied?"));
        }

        _userRepository.Insert(user);
        _userRoleRepository.Insert(new UserRole(user.Id, userRole.Id, DateTime.UtcNow));
        await _userRepository.SaveChangesAsync(cancellationToken);

        var response = new CreateUserResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email.Value,
            user.IsActive,
            [userRole.Name],
            user.Locations.Select(l => l.Name).ToArray()
        );

        return Result.Success(response);
    }
}
