using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Users.Commands;
using Features.Users.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Users.Endpoints;

public class GetUserEndpoint : Endpoint<GetUserRequest, GetUserResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetUserEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/users/{id}");
        Tags("Users");
        Description(b => b.WithTags("Users"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Get user by ID";
            s.Description = "Retrieve a user by ID. Users can view their own account; administrators can view any account. Other accounts appear as not found.";
            s.Response(200, "User found successfully");
            s.Response(401, "Not signed in");
            s.Response(404, "User not found");
        });
    }

    public override async Task HandleAsync(GetUserRequest req, CancellationToken ct)
    {
        var query = new GetUserQuery(req.Id, Caller.From(User));
        Result<GetUserResponse> result = await _dispatcher.Send(query, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(404);
        }

        Response = result.Value;
    }
}

public record GetUserRequest(Guid Id);
