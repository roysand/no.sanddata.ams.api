using Domain.Common;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using FastEndpoints;
using MediatR;

namespace Features.Users;

public static class GetUser
{
    public record GetUserRequest(Guid Id) : IRequest<Result<UserResponse>>;

    public record UserResponse(
        Guid Id,
        string FirstName,
        string LastName,
        string Email,
        bool IsActive,
        string[] Roles,
        string[] Locations
    );

    internal sealed class Handler : IRequestHandler<GetUserRequest, Result<UserResponse>>
    {
        private readonly IUserEfRepository<User> _userRepository;

        public Handler(IUserEfRepository<User> userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<Result<UserResponse>> Handle(
            GetUserRequest request,
            CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(request.Id, cancellationToken);

            if (user is null)
            {
                return Result.Failure<UserResponse>(
                    Error.NotFound("User.NotFound", $"User with ID {request.Id} was not found"));
            }

            var response = new UserResponse(
                user.Id,
                user.FirstName,
                user.LastName,
                user.Email.Value,
                user.IsActive,
                user.Roles.Select(r => r.Name).ToArray(),
                user.Locations.Select(l => l.Name).ToArray()
            );

            return Result.Success(response);
        }
    }
}

public class GetUserEndpoint : Endpoint<GetUser.GetUserRequest, GetUser.UserResponse>
{
    private readonly ISender _sender;

    public GetUserEndpoint(ISender sender) => _sender = sender;

    public override void Configure()
    {
        Get("/api/users/{id}");
        AllowAnonymous(); // TODO: Add authorization - users can view their own profile, admins can view any
        // Policies(new[] { "UserOrAdmin" });
        Summary(s =>
        {
            s.Summary = "Get user by ID";
            s.Description = "Retrieve a specific user's details by their ID";
            s.Response(200, "User found successfully");
            s.Response(404, "User not found");
        });
    }

    public override async Task HandleAsync(
        GetUser.GetUserRequest req,
        CancellationToken ct)
    {
        var result = await _sender.Send(req, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors(404);
        }

        Response = result.Value;
    }
}
