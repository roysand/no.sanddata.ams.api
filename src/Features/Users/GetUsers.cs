using Domain.Common;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using FastEndpoints;
using FluentValidation;
using MediatR;

namespace Features.Users;

public static class GetUsers
{
    public record GetUsersRequest(
        int PageNumber = 1,
        int PageSize = 10,
        bool? IsActive = null,
        string? Search = null
    ) : IRequest<Result<PagedUsersResponse>>;

    public record PagedUsersResponse(
        UserResponse[] Users,
        int TotalCount,
        int PageNumber,
        int PageSize,
        int TotalPages
    );

    public record UserResponse(
        Guid Id,
        string FirstName,
        string LastName,
        string Email,
        bool IsActive,
        string[] Roles,
        string[] Locations
    );

    internal sealed class Handler : IRequestHandler<GetUsersRequest, Result<PagedUsersResponse>>
    {
        private readonly IUserEfRepository<User> _userRepository;

        public Handler(IUserEfRepository<User> userRepository) => _userRepository = userRepository;

        public async Task<Result<PagedUsersResponse>> Handle(
            GetUsersRequest request,
            CancellationToken cancellationToken)
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
            UserResponse[] pagedUsers = filteredUsers
                .Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize)
                .Select(u => new UserResponse(
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

    public class GetUsersValidator : Validator<GetUsersRequest>
    {
        public GetUsersValidator()
        {
            RuleFor(x => x.PageNumber)
                .GreaterThan(0).WithMessage("Page number must be greater than 0");

            RuleFor(x => x.PageSize)
                .GreaterThan(0).WithMessage("Page size must be greater than 0")
                .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100");
        }
    }
}

public class GetUsersEndpoint : EndpointWithoutRequest<GetUsers.PagedUsersResponse>
{
    private readonly ISender _sender;

    public GetUsersEndpoint(ISender sender) => _sender = sender;

    public override void Configure()
    {
        Get("/api/users");
        AllowAnonymous(); // TODO: Add authorization - typically admin only
        // Policies(new[] { "AdminOnly" });
        Summary(s =>
        {
            s.Summary = "Get all users";
            s.Description = "Retrieve a paginated list of users with optional filtering";
            s.Params["PageNumber"] = "The page number to retrieve (default: 1, must be > 0)";
            s.Params["PageSize"] = "Number of items per page (default: 10, max: 100)";
            s.Params["IsActive"] = "Filter by active status (optional)";
            s.Params["Search"] = "Search term to filter users by name or email (optional)";
            s.Response(200, "Users retrieved successfully");
            s.Response(400, "Invalid request parameters");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        int pageNumber = Query<int>("PageNumber", isRequired: false);
        int pageSize = Query<int>("PageSize", isRequired: false);
        bool? isActive = Query<bool?>("IsActive", isRequired: false);
        string? search = Query<string?>("Search", isRequired: false);

        var request = new GetUsers.GetUsersRequest(
            PageNumber: pageNumber == 0 ? 1 : pageNumber,
            PageSize: pageSize == 0 ? 10 : pageSize,
            IsActive: isActive,
            Search: search
        );

        Result<GetUsers.PagedUsersResponse> result = await _sender.Send(request, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors(400);
        }

        Response = result.Value;
    }
}
