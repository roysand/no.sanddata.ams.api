using Domain.Common;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using FastEndpoints;
using MediatR;

namespace Features.Roles;

public static class GetRole
{
    public record GetRoleRequest(Guid Id) : IRequest<Result<RoleResponse>>;

    public record RoleResponse(
        Guid Id,
        string Name,
        string Description,
        bool IsActive
    );

    internal sealed class Handler : IRequestHandler<GetRoleRequest, Result<RoleResponse>>
    {
        private readonly IRoleEfRepository<Role> _roleRepository;

        public Handler(IRoleEfRepository<Role> roleRepository) => _roleRepository = roleRepository;

        public async Task<Result<RoleResponse>> Handle(
            GetRoleRequest request,
            CancellationToken cancellationToken)
        {
            Role? role = await _roleRepository.GetByIdAsync(request.Id, cancellationToken);

            if (role is null)
            {
                return Result.Failure<RoleResponse>(
                    Error.NotFound("Role.NotFound", $"Role with ID {request.Id} was not found"));
            }

            var response = new RoleResponse(
                role.Id,
                role.Name,
                role.Description,
                role.IsActive
            );

            return Result.Success(response);
        }
    }
}

public class GetRoleEndpoint : Endpoint<GetRole.GetRoleRequest, GetRole.RoleResponse>
{
    private readonly ISender _sender;

    public GetRoleEndpoint(ISender sender) => _sender = sender;

    public override void Configure()
    {
        Get("/api/roles/{id}");
        AllowAnonymous(); // TODO: Add authorization as needed
        // Policies(new[] { "AdminOnly" });
        Summary(s =>
        {
            s.Summary = "Get role by ID";
            s.Description = "Retrieve a specific role's details by their ID";
            s.Response(200, "Role found successfully");
            s.Response(404, "Role not found");
        });
    }

    public override async Task HandleAsync(
        GetRole.GetRoleRequest req,
        CancellationToken ct)
    {
        Result<GetRole.RoleResponse> result = await _sender.Send(req, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors(404);
        }

        Response = result.Value;
    }
}
