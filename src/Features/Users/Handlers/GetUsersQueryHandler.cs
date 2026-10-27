using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Domain.Common;
using Domain.Common.Entities;
using Features.Users.Mappers;
using Features.Users.Queries;

namespace Features.Users.Handlers;

public class GetUsersQueryHandler : IQueryHandler<GetUsersQuery, Result<PagedUsersResponse>>
{
    private readonly IUserRepository<User> _userRepository;
    private readonly IUserLocationRepository<UserLocation> _userLocationRepository;

    public GetUsersQueryHandler(
        IUserRepository<User> userRepository,
        IUserLocationRepository<UserLocation> userLocationRepository)
    {
        _userRepository = userRepository;
        _userLocationRepository = userLocationRepository;
    }

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
        var page = filteredUsers
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToList();

        // One query for the role of every link on this page.
        IReadOnlyList<UserLinkInfo> links = await _userLocationRepository.GetForUsersAsync(
            page.Select(u => u.Id).ToList(), cancellationToken);
        ILookup<Guid, UserLinkInfo> linksByUser = links.ToLookup(l => l.UserId);

        UserListResponse[] pagedUsers = page
            .Select(u => UserMapper.ToListResponse(u, linksByUser[u.Id]))
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
