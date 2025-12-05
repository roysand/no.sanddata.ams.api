using Domain.Common;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using FastEndpoints;
using MediatR;

namespace Features.Users;

public static class DeleteUser
{
    public record DeleteUserRequest(Guid Id) : IRequest<Result<bool>>;

    internal sealed class Handler : IRequestHandler<DeleteUserRequest, Result<bool>>
    {
        private readonly IUserEfRepository<User> _userRepository;

        public Handler(IUserEfRepository<User> userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<Result<bool>> Handle(
            DeleteUserRequest request,
            CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(request.Id, cancellationToken);

            if (user is null)
            {
                return Result.Failure<bool>(
                    Error.NotFound("User.NotFound", $"User with ID {request.Id} was not found"));
            }

            // Soft delete - set IsActive to false
            user.IsActive = false;
            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync(cancellationToken);

            // For hard delete, use:
            // _userRepository.Delete(user);
            // await _userRepository.SaveChangesAsync(cancellationToken);

            return Result.Success(true);
        }
    }
}

public class DeleteUserEndpoint : EndpointWithoutRequest
{
    private readonly ISender _sender;

    public DeleteUserEndpoint(ISender sender) => _sender = sender;

    public override void Configure()
    {
        Delete("/api/users/{id}");
        AllowAnonymous(); // TODO: Add authorization - typically admin only
        // Policies(new[] { "AdminOnly" });
        Summary(s =>
        {
            s.Summary = "Delete user";
            s.Description = "Soft delete a user by setting IsActive to false. The user will no longer be able to log in.";
            s.Response(204, "User deleted successfully");
            s.Response(404, "User not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("id");
        var request = new DeleteUser.DeleteUserRequest(id);
        var result = await _sender.Send(request, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors(404);
        }

        // Success - send 204 No Content
        HttpContext.Response.StatusCode = 204;
    }
}
