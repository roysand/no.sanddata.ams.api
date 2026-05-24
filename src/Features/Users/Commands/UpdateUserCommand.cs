using Application.CQRS;
using Domain.Common;

namespace Features.Users.Commands;

public record UpdateUserCommand(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    bool IsActive
) : ICommand<Result<UpdateUserResponse>>;

public record UpdateUserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    bool IsActive,
    string[] Roles,
    string[] Locations
);
