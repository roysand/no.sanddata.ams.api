using Application.CQRS;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Commands;
using Microsoft.Extensions.Logging;
using Features.Users.Logging;
using Infrastructure.Logging;

namespace Features.Users.Handlers;

public class DeleteUserCommandHandler : ICommandHandler<DeleteUserCommand, Result<DeleteUserResponse>>
{
    private readonly IUserEfRepository<User> _userRepository;
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(IUserEfRepository<User> userRepository, ILogger<DeleteUserCommandHandler> logger) => (_userRepository, _logger) = (userRepository, logger);

    public async Task<Result<DeleteUserResponse>> Handle(DeleteUserCommand command, CancellationToken cancellationToken)
    {
        User? user = await _userRepository.GetByIdAsync(command.Id, cancellationToken);

        if (user is null)
        {
            LogMessages.UserNotFound(_logger, command.Id);

            return Result.Failure<DeleteUserResponse>(
                Error.NotFound("User.NotFound", $"User with ID {command.Id} was not found"));
        }

        _userRepository.Delete(user);
        await _userRepository.SaveChangesAsync(cancellationToken);

        return Result.Success(new DeleteUserResponse(true));
    }
}
