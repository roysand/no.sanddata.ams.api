using Domain.Common;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using FastEndpoints;
using FluentValidation;
using MediatR;

namespace Features.Roles;

public static class GetRoles
{
    public record GetRolesRequest(
        int PageNumber = 1,
        int PageSize = 10,
        bool? IsActive = null,
        string? Search = null
    ) : IRequest<Result<PagedRolesResponse>>;

    public record PagedRolesResponse(
        RoleResponse[] Roles,
        int TotalCount,
        int PageNumber,
        int PageSize,
        int TotalPages
    );

    public record RoleResponse(
        Guid Id,
        string Name,
        string Description,
        bool IsActive
    );

    internal sealed class Handler : IRequestHandler<GetRolesRequest, Result<PagedRolesResponse>>
    {
        private readonly IRoleEfRepository<Role> _roleRepository;

        public Handler(IRoleEfRepository<Role> roleRepository) => _roleRepository = roleRepository;

        public async Task<Result<PagedRolesResponse>> Handle(
            GetRolesRequest request,
            CancellationToken cancellationToken)
        {
            // Build the filter predicate
            IEnumerable<Role?> roles = await _roleRepository.FindAsync(
                r => (request.IsActive == null || r.IsActive == request.IsActive) &&
                     (string.IsNullOrEmpty(request.Search) ||
                      r.Name.Contains(request.Search) ||
                      r.Description.Contains(request.Search)),
                cancellationToken);

            // Filter out nulls and get total count
            var filteredRoles = roles.OfType<Role>().ToList();
            int totalCount = filteredRoles.Count;

            // Apply pagination
            RoleResponse[] pagedRoles = filteredRoles
                .Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize)
                .Select(r => new RoleResponse(
                    r.Id,
                    r.Name,
                    r.Description,
                    r.IsActive
                ))
                .ToArray();

            int totalPages = (int)Math.Ceiling(totalCount / (double)request.PageSize);

            var response = new PagedRolesResponse(
                pagedRoles,
                totalCount,
                request.PageNumber,
                request.PageSize,
                totalPages
            );

            return Result.Success(response);
        }
    }

    public class GetRolesValidator : Validator<GetRolesRequest>
    {
        public GetRolesValidator()
        {
            RuleFor(x => x.PageNumber)
                .GreaterThan(0).WithMessage("Page number must be greater than 0");

            RuleFor(x => x.PageSize)
                .GreaterThan(0).WithMessage("Page size must be greater than 0")
                .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100");
        }
    }
}

public class GetRolesEndpoint : EndpointWithoutRequest<GetRoles.PagedRolesResponse>
{
    private readonly ISender _sender;

    public GetRolesEndpoint(ISender sender) => _sender = sender;

    public override void Configure()
    {
        Get("/api/roles");
        AllowAnonymous(); // TODO: Add authorization as needed
        // Policies(new[] { "AdminOnly" });
        Summary(s =>
        {
            s.Summary = "Get all roles";
            s.Description = "Retrieve a paginated list of roles with optional filtering";
            s.Params["PageNumber"] = "The page number to retrieve (default: 1, must be > 0)";
            s.Params["PageSize"] = "Number of items per page (default: 10, max: 100)";
            s.Params["IsActive"] = "Filter by active status (optional)";
            s.Params["Search"] = "Search term to filter roles by name or description (optional)";
            s.Response(200, "Roles retrieved successfully");
            s.Response(400, "Invalid request parameters");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        int pageNumber = Query<int>("PageNumber", isRequired: false);
        int pageSize = Query<int>("PageSize", isRequired: false);
        bool? isActive = Query<bool?>("IsActive", isRequired: false);
        string? search = Query<string?>("Search", isRequired: false);

        var request = new GetRoles.GetRolesRequest(
            PageNumber: pageNumber == 0 ? 1 : pageNumber,
            PageSize: pageSize == 0 ? 10 : pageSize,
            IsActive: isActive,
            Search: search
        );

        Result<GetRoles.PagedRolesResponse> result = await _sender.Send(request, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors(400);
        }

        Response = result.Value;
    }
}
