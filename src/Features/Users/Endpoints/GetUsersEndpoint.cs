using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Users.Queries;

namespace Features.Users.Endpoints;

public class GetUsersEndpoint : Endpoint<GetUsersRequest, PagedUsersResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetUsersEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/users");
        AllowAnonymous();
        // GET has no body; restrict binding to the query string so a stray "Content-Type: application/json"
        // header with an empty body (sent by some clients, e.g. Scalar) doesn't fail JSON deserialization.
        RequestBinder(new RequestBinder<GetUsersRequest>(BindingSource.QueryParams));
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

    public override async Task HandleAsync(GetUsersRequest req, CancellationToken ct)
    {
        var query = new GetUsersQuery(
            PageNumber: req.PageNumber == 0 ? 1 : req.PageNumber,
            PageSize: req.PageSize == 0 ? 10 : req.PageSize,
            IsActive: req.IsActive,
            Search: req.Search
        );

        Result<PagedUsersResponse> result = await _dispatcher.Send(query, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors(400);
        }

        Response = result.Value;
    }
}

public record GetUsersRequest(
    [property: QueryParam] int PageNumber = 1,
    [property: QueryParam] int PageSize = 10,
    [property: QueryParam] bool? IsActive = null,
    [property: QueryParam] string? Search = null
);
