using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Logging;
using Features.Users.Queries;
using Microsoft.Extensions.Logging;

namespace Features.Users.Handlers;

public class GetUserQueryHandler : IQueryHandler<GetUserQuery, Result<GetUserResponse>>
{
    private readonly IUserRepository<User> _userRepository;
    private readonly ILogger<GetUserQueryHandler> _logger;

    public GetUserQueryHandler(IUserRepository<User> userRepository, ILogger<GetUserQueryHandler> logger)
    {
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<Result<GetUserResponse>> Handle(GetUserQuery request, CancellationToken cancellationToken)
    {
        // FindAsync(predicate), not GetByIdAsync: Find ignores AutoInclude, and the response needs roles/locations.
        User? user = (await _userRepository.FindAsync(u => u.Id == request.Id, cancellationToken, noTrack: true))
            .FirstOrDefault();

        // A non-Admin asking for another account gets the same answer as for a missing account.
        if (user is not null && !request.Caller.IsAdmin && request.Caller.Id != request.Id)
        {
            LogMessages.UserAccessDenied(_logger, request.Id, request.Caller.Id);
            user = null;
        }

        if (user is null)
        {
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
