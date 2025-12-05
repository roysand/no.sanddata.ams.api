using Domain.Common;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using FastEndpoints;
using FluentValidation;
using MediatR;

namespace Features.Roles;

public static class CreateRole
{
    public record CreateRoleRequest(
        string Name,
        string Description,
        bool IsActive = true
    ) : IRequest<Result<RoleResponse>>;

    public record RoleResponse(
        Guid Id,
        string Name,
        string Description,
        bool IsActive
    );

    internal sealed class Handler : IRequestHandler<CreateRoleRequest, Result<RoleResponse>>
    {
        private readonly IRoleEfRepository<Role> _roleRepository;

        public Handler(IRoleEfRepository<Role> roleRepository) => _roleRepository = roleRepository;

        public async Task<Result<RoleResponse>> Handle(
            CreateRoleRequest request,
            CancellationToken cancellationToken)
        {
            // Check if role name already exists
            IEnumerable<Role?> existingRoles = await _roleRepository.FindAsync(
                r => r.Name == request.Name,
                cancellationToken);

            if (existingRoles.Any())
            {
                return Result.Failure<RoleResponse>(
                    Error.Conflict("Role.NameExists", "A role with this name already exists"));
            }

            // Create new role
            var role = new Role(
                Guid.NewGuid(),
                request.Name,
                request.Description,
                request.IsActive
            );

            _roleRepository.Insert(role);
            await _roleRepository.SaveChangesAsync(cancellationToken);

            var response = new RoleResponse(
                role.Id,
                role.Name,
                role.Description,
                role.IsActive
            );

            return Result.Success(response);
        }
    }

    public class CreateRoleValidator : Validator<CreateRoleRequest>
    {
        public CreateRoleValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Role name is required")
                .MaximumLength(100).WithMessage("Role name must not exceed 100 characters");

            RuleFor(x => x.Description)
                .NotEmpty().WithMessage("Description is required")
                .MaximumLength(500).WithMessage("Description must not exceed 500 characters");
        }
    }
}

public class CreateRoleEndpoint : Endpoint<CreateRole.CreateRoleRequest, CreateRole.RoleResponse>
{
    private readonly ISender _sender;

    public CreateRoleEndpoint(ISender sender) => _sender = sender;

    public override void Configure()
    {
        Post("/api/roles");
        AllowAnonymous(); // TODO: Add authorization - typically admin only
        // Policies(new[] { "AdminOnly" });
        Summary(s =>
        {
            s.Summary = "Create a new role";
            s.Description = "Create a new role in the system with a name and description.";
            s.ExampleRequest = new CreateRole.CreateRoleRequest(
                "Administrator",
                "Full system access with all permissions",
                true
            );
            s.Response(201, "Role created successfully");
            s.Response(409, "Role with this name already exists");
            s.Response(400, "Invalid request data");
        });
    }

    public override async Task<CreateRole.RoleResponse> HandleAsync(
        CreateRole.CreateRoleRequest req,
        CancellationToken ct)
    {
        Result<CreateRole.RoleResponse> result = await _sender.Send(req, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors(result.Error.Type switch
            {
                ErrorType.Conflict => 409,
                ErrorType.Validation => 400,
                _ => 400
            });
        }

        // Set 201 Created status and Location header
        HttpContext.Response.StatusCode = 201;
        HttpContext.Response.Headers.Location = $"/api/roles/{result.Value.Id}";

        return result.Value;
    }
}
