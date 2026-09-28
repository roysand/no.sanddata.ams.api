using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Users.Mappers;
using Features.Users.Queries;

namespace Features.Users.Endpoints;

public class GetUserEndpoint : Endpoint<GetUserRequest, GetUserResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetUserEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/users/{id}");
        AllowAnonymous();
        // GET has no body; restrict binding to the route so a stray "Content-Type: application/json"
        // header with an empty body (sent by some clients, e.g. Scalar) doesn't fail JSON deserialization.
        RequestBinder(new RequestBinder<GetUserRequest>(BindingSource.RouteValues));
        Summary(s =>
        {
            s.Summary = "Get user by ID";
            s.Description = "Retrieve a specific user's details by their ID";
            s.Response(200, "User found successfully");
            s.Response(404, "User not found");
        });
    }

    public override async Task HandleAsync(GetUserRequest req, CancellationToken ct)
    {
        var query = new GetUserQuery(req.Id);
        Result<GetUserResponse> result = await _dispatcher.Send(query, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors(404);
        }

        Response = result.Value;
    }
}

public record GetUserRequest([property: RouteParam] Guid Id);
