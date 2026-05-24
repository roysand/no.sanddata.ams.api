using Application.CQRS;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Commands;

namespace Features.Users.Handlers;

public class DeleteUserCommandHandler : ICommandHandler<DeleteUserCommand, Result<DeleteUserResponse>>
{
    private readonly IUserEfRepository<User> _userRepository;

    public DeleteUserCommandHandler(IUserEfRepository<User> userRepository) => _userRepository = userRepository;

    public async Task<Result<DeleteUserResponse>> Handle(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        User? user = await _userRepository.GetByIdAsync(command.Id, cancellationToken);

        if (user is null)
        {
            return Result.Failure<DeleteUserResponse>(
                Error.NotFound("User.NotFound", $"User with ID {command.Id} was not found"));
        }

        _userRepository.Delete(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(new DeleteUserResponse(true));
    }
}
