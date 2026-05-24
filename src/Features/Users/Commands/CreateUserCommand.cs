using Application.CQRS;
using Domain.Common;

namespace Features.Users.Commands;

public record CreateUserCommand(
    string FirstName,
    string LastName,
    string Email,
    string Password
) : ICommand<Result<CreateUserResponse>>;

public record CreateUserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    bool IsActive,
    string[] Roles,
    string[] Locations
);
