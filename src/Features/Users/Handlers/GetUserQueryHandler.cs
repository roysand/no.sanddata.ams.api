using Application.CQRS;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Queries;
using Microsoft.Extensions.Logging;
using Features.Users.Logging;
using Infrastructure.Logging;

namespace Features.Users.Handlers;

public class GetUserQueryHandler : IQueryHandler<GetUserQuery, Result<GetUserResponse>>
{
    private readonly IUserEfRepository<User> _userRepository;
    private readonly ILogger<GetUserQueryHandler> _logger;

    public GetUserQueryHandler(IUserEfRepository<User> userRepository, ILogger<GetUserQueryHandler> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<Result<GetUserResponse>> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        User? user = await _userRepository.GetByIdAsync(request.Id, cancellationToken);

        if (user is null)
        {
            // Log user not found
            LogMessages.UserNotFound(_logger, request.Id);

            return Result.Failure<GetUserResponse>(
                Error.NotFound("User.NotFound", $"User with ID {request.Id} was not found"));
        }

        var response = new GetUserResponse(
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
