using Application.CQRS;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Commands;
using Infrastructure.Authentication;

namespace Features.Users.Handlers;

public class ChangePasswordCommandHandler : ICommandHandler<ChangePasswordCommand, Result<ChangePasswordResponse>>
{
    private readonly IUserEfRepository<User> _userRepository;
    private readonly IPasswordHasher _passwordHasher;

    public ChangePasswordCommandHandler(IUserEfRepository<User> userRepository, IPasswordHasher passwordHasher)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
    }

    public async Task<Result<ChangePasswordResponse>> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        User? user = await _userRepository.GetByIdAsync(command.Id, cancellationToken);

        if (user is null)
        {
            return Result.Failure<ChangePasswordResponse>(
                Error.NotFound("User.NotFound", $"User with ID {command.Id} was not found"));
        }

        if (!user.IsActive)
        {
            return Result.Failure<ChangePasswordResponse>(
                Error.Validation("User.Inactive", "User account is inactive"));
        }

        // Verify current password using BCrypt
        if (!_passwordHasher.VerifyPassword(command.CurrentPassword, user.PasswordHash))
        {
            return Result.Failure<ChangePasswordResponse>(
                Error.Validation("User.InvalidPassword", "Current password is incorrect"));
        }

        // Hash the new password using BCrypt
        user.PasswordHash = _passwordHasher.HashPassword(command.NewPassword);

        _userRepository.Update(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(new ChangePasswordResponse(true));
    }
}
