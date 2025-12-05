using Domain.Common;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using FastEndpoints;
using FluentValidation;
using MediatR;

namespace Features.Roles;

public static class UpdateRole
{
    public record UpdateRoleRequest(
        Guid Id,
        string Name,
        string Description,
        bool IsActive
    ) : IRequest<Result<RoleResponse>>;

    public record RoleResponse(
        Guid Id,
        string Name,
        string Description,
        bool IsActive
    );

    internal sealed class Handler : IRequestHandler<UpdateRoleRequest, Result<RoleResponse>>
    {
        private readonly IRoleEfRepository<Role> _roleRepository;

        public Handler(IRoleEfRepository<Role> roleRepository) => _roleRepository = roleRepository;

        public async Task<Result<RoleResponse>> Handle(
            UpdateRoleRequest request,
            CancellationToken cancellationToken)
        {
            Role? role = await _roleRepository.GetByIdAsync(request.Id, cancellationToken);

            if (role is null)
            {
                return Result.Failure<RoleResponse>(
                    Error.NotFound("Role.NotFound", $"Role with ID {request.Id} was not found"));
            }

            // Check if name is being changed and if it's already taken by another role
            if (role.Name != request.Name)
            {
                IEnumerable<Role?> existingRoles = await _roleRepository.FindAsync(
                    r => r.Name == request.Name && r.Id != request.Id,
                    cancellationToken);

                if (existingRoles.Any())
                {
                    return Result.Failure<RoleResponse>(
                        Error.Conflict("Role.NameExists", "A role with this name already exists"));
                }
            }

            // Update role properties using reflection since properties have private setters
            System.Reflection.PropertyInfo? nameProperty = typeof(Role).GetProperty(nameof(Role.Name));
            System.Reflection.PropertyInfo? descriptionProperty = typeof(Role).GetProperty(nameof(Role.Description));
            System.Reflection.PropertyInfo? isActiveProperty = typeof(Role).GetProperty(nameof(Role.IsActive));

            nameProperty?.SetValue(role, request.Name);
            descriptionProperty?.SetValue(role, request.Description);
            isActiveProperty?.SetValue(role, request.IsActive);

            _roleRepository.Update(role);
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

    public class UpdateRoleValidator : Validator<UpdateRoleRequest>
    {
        public UpdateRoleValidator()
        {
            RuleFor(x => x.Id)
                .NotEmpty().WithMessage("Role ID is required");

            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Role name is required")
                .MaximumLength(100).WithMessage("Role name must not exceed 100 characters");

            RuleFor(x => x.Description)
                .NotEmpty().WithMessage("Description is required")
                .MaximumLength(500).WithMessage("Description must not exceed 500 characters");
        }
    }
}

public class UpdateRoleEndpoint : Endpoint<UpdateRole.UpdateRoleRequest, UpdateRole.RoleResponse>
{
    private readonly ISender _sender;

    public UpdateRoleEndpoint(ISender sender) => _sender = sender;

    public override void Configure()
    {
        Put("/api/roles/{id}");
        AllowAnonymous(); // TODO: Add authorization - typically admin only
        // Policies(new[] { "AdminOnly" });
        Summary(s =>
        {
            s.Summary = "Update role";
            s.Description = "Update an existing role's information";
            s.ExampleRequest = new UpdateRole.UpdateRoleRequest(
                Guid.NewGuid(),
                "Administrator",
                "Full system access with all permissions",
                true
            );
            s.Response(200, "Role updated successfully");
            s.Response(404, "Role not found");
            s.Response(409, "Role name already exists");
            s.Response(400, "Invalid request data");
        });
    }

    public override async Task HandleAsync(
        UpdateRole.UpdateRoleRequest req,
        CancellationToken ct)
    {
        Result<UpdateRole.RoleResponse> result = await _sender.Send(req, ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Code, result.Error.Description);
            ThrowIfAnyErrors(result.Error.Type switch
            {
                ErrorType.NotFound => 404,
                ErrorType.Conflict => 409,
                ErrorType.Validation => 400,
                _ => 400
            });
        }

        Response = result.Value;
    }
}
