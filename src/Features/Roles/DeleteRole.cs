using Domain.Common;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using FastEndpoints;
using MediatR;

namespace Features.Roles;

public static class DeleteRole
{
    public record DeleteRoleRequest(Guid Id) : IRequest<Result<bool>>;

    internal sealed class Handler : IRequestHandler<DeleteRoleRequest, Result<bool>>
    {
        private readonly IRoleEfRepository<Role> _roleRepository;

        public Handler(IRoleEfRepository<Role> roleRepository) => _roleRepository = roleRepository;

        public async Task<Result<bool>> Handle(
            DeleteRoleRequest request,
            CancellationToken cancellationToken)
        {
            Role? role = await _roleRepository.GetByIdAsync(request.Id, cancellationToken);

            if (role is null)
            {
                return Result.Failure<bool>(
                    Error.NotFound("Role.NotFound", $"Role with ID {request.Id} was not found"));
            }

            _roleRepository.Delete(role);
            await _roleRepository.SaveChangesAsync(cancellationToken);

            return Result.Success(true);
        }
    }
}

public class DeleteRoleEndpoint : EndpointWithoutRequest
{
    private readonly ISender _sender;

    public DeleteRoleEndpoint(ISender sender) => _sender = sender;

    public override void Configure()
    {
        Delete("/api/roles/{id}");
        AllowAnonymous(); // TODO: Add authorization - typically admin only
        // Policies(new[] { "AdminOnly" });
        Summary(s =>
        {
            s.Summary = "Delete role";
            s.Description = "Delete a role from the system. Note: This will remove the role permanently.";
            s.Response(204, "Role deleted successfully");
            s.Response(404, "Role not found");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        Guid id = Route<Guid>("id");
        var request = new DeleteRole.DeleteRoleRequest(id);
        Result<bool> result = await _sender.Send(request, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors(404);
        }

        // Success - send 204 No Content
        HttpContext.Response.StatusCode = 204;
    }
}
