using Application.CQRS;
using Domain.Common;

namespace Features.Users.Queries;

public record GetUsersQuery(
    int PageNumber = 1,
    int PageSize = 10,
    bool? IsActive = null,
    string? Search = null
) : IQuery<Result<PagedUsersResponse>>;

public record PagedUsersResponse(
    UserListResponse[] Users,
    int TotalCount,
    int PageNumber,
    int PageSize,
    int TotalPages
);

public record UserListResponse(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    bool IsActive,
    string[] Roles,
    string[] Locations
);
