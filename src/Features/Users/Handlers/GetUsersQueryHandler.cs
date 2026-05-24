using Application.CQRS;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Queries;

namespace Features.Users.Handlers;

public class GetUsersQueryHandler : IQueryHandler<GetUsersQuery, Result<PagedUsersResponse>>
{
    private readonly IUserEfRepository<User> _userRepository;

    public GetUsersQueryHandler(IUserEfRepository<User> userRepository) 
        => _userRepository = userRepository;

    public async Task<Result<PagedUsersResponse>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        // Build the filter predicate
        IEnumerable<User?> users = await _userRepository.FindAsync(
            u => (request.IsActive == null || u.IsActive == request.IsActive) &&
                 (string.IsNullOrEmpty(request.Search) ||
                  u.FirstName.Contains(request.Search) ||
                  u.LastName.Contains(request.Search) ||
                  u.Email.Value.Contains(request.Search)),
            cancellationToken);

        // Filter out nulls and get total count
        var filteredUsers = users.OfType<User>().ToList();
        int totalCount = filteredUsers.Count;

        // Apply pagination
        UserListResponse[] pagedUsers = filteredUsers
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(u => new UserListResponse(
                u.Id,
                u.FirstName,
                u.LastName,
                u.Email.Value,
                u.IsActive,
                u.Roles.Select(r => r.Name).ToArray(),
                u.Locations.Select(l => l.Name).ToArray()
            ))
            .ToArray();

        int totalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize);

        var response = new PagedUsersResponse(
            pagedUsers,
            totalCount,
            request.PageNumber,
            request.PageSize,
            totalPages
        );

        return Result.Success(response);
    }
}
