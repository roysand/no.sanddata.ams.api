using Application.CQRS;
using Domain.Common;

namespace Features.Users.Queries;

public record GetUserQuery(Guid Id) : IQuery<Result<GetUserResponse>>;

public record GetUserResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    bool IsActive,
    string[] Roles,
    string[] Locations
);
