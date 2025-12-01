using Domain.Common;
using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Domain.Common.ValueObjects;
using FastEndpoints;
using FluentValidation;
using MediatR;

namespace Features.Users;

public static class UpdateUser
{
    public record UpdateUserRequest(
        Guid Id,
        string FirstName,
        string LastName,
        string Email,
        bool IsActive
    ) : IRequest<Result<UserResponse>>;

    public record UserResponse(
        Guid Id,
        string FirstName,
        string LastName,
        string Email,
        bool IsActive,
        string[] Roles,
        string[] Locations
    );

    internal sealed class Handler : IRequestHandler<UpdateUserRequest, Result<UserResponse>>
    {
        private readonly IUserEfRepository<User> _userRepository;

        public Handler(IUserEfRepository<User> userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<Result<UserResponse>> Handle(
            UpdateUserRequest request,
            CancellationToken cancellationToken)
        {
            var user = await _userRepository.GetByIdAsync(request.Id, cancellationToken);

            if (user is null)
            {
                return Result.Failure<UserResponse>(
                    Error.NotFound("User.NotFound", $"User with ID {request.Id} was not found"));
            }

            // Check if email is being changed and if it's already taken by another user
            if (user.Email.Value != request.Email)
            {
                var existingUsers = await _userRepository.FindAsync(
                    u => u.Email.Value == request.Email && u.Id != request.Id,
                    cancellationToken);

                if (existingUsers.Any())
                {
                    return Result.Failure<UserResponse>(
                        Error.Conflict("User.EmailExists", "A user with this email already exists"));
                }

                // Create new email value object using factory pattern
                var emailResult = EmailAddress.Create(request.Email);
                if (emailResult.IsFailure)
                {
                    return Result.Failure<UserResponse>(emailResult.Error);
                }

                user.Email = emailResult.Value;
            }

            // Update user properties
            user.FirstName = request.FirstName;
            user.LastName = request.LastName;
            user.IsActive = request.IsActive;

            _userRepository.Update(user);
            await _userRepository.SaveChangesAsync(cancellationToken);

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

    public class UpdateUserValidator : Validator<UpdateUserRequest>
    {
        public UpdateUserValidator()
        {
            RuleFor(x => x.Id)
                .NotEmpty().WithMessage("User ID is required");

            RuleFor(x => x.FirstName)
                .NotEmpty().WithMessage("First name is required")
                .MaximumLength(100).WithMessage("First name must not exceed 100 characters");

            RuleFor(x => x.LastName)
                .NotEmpty().WithMessage("Last name is required")
                .MaximumLength(100).WithMessage("Last name must not exceed 100 characters");

            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required")
                .EmailAddress().WithMessage("A valid email is required")
                .MaximumLength(255).WithMessage("Email must not exceed 255 characters");
        }
    }
}

public class UpdateUserEndpoint : Endpoint<UpdateUser.UpdateUserRequest, UpdateUser.UserResponse>
{
    private readonly ISender _sender;

    public UpdateUserEndpoint(ISender sender) => _sender = sender;

    public override void Configure()
    {
        Put("/api/users/{id}");
        AllowAnonymous(); // TODO: Add authorization - users can update their own profile, admins can update any
        // Policies(new[] { "UserOrAdmin" });
        Summary(s =>
        {
            s.Summary = "Update user";
            s.Description = "Update an existing user's information";
            s.ExampleRequest = new UpdateUser.UpdateUserRequest(
                Guid.NewGuid(),
                "John",
                "Doe",
                "john.doe@example.com",
                true
            );
            s.Response(200, "User updated successfully");
            s.Response(404, "User not found");
            s.Response(409, "Email already exists");
            s.Response(400, "Invalid request data");
        });
    }

    public override async Task HandleAsync(
        UpdateUser.UpdateUserRequest req,
        CancellationToken ct)
    {
        var result = await _sender.Send(req, ct);

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
