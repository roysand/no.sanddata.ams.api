using Domain.Common.Entities;
using Features.Users.Commands;
using Features.Users.Endpoints;
using Features.Users.Queries;

namespace Features.Users.Mappers;

public static class UserMapper
{
    // Request → Command
    public static CreateUserCommand ToCreateCommand(CreateUserRequest request) =>
        new CreateUserCommand(request.FirstName, request.LastName, request.Email, request.Password);

    public static UpdateUserCommand ToUpdateCommand(Guid id, UpdateUserRequest request) =>
        new UpdateUserCommand(id, request.FirstName, request.LastName, request.Email, request.IsActive);

    // Domain Entity → Response
    public static CreateUserResponse ToCreateResponse(User user) =>
        new CreateUserResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email.Value,
            user.IsActive,
            user.Roles.Select(r => r.Name).ToArray(),
            user.Locations.Select(l => l.Name).ToArray());

    public static UpdateUserResponse ToUpdateResponse(User user) =>
        new UpdateUserResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email.Value,
            user.IsActive,
            user.Roles.Select(r => r.Name).ToArray(),
            user.Locations.Select(l => l.Name).ToArray());

    public static GetUserResponse ToGetResponse(User user) =>
        new GetUserResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email.Value,
            user.IsActive,
            user.Roles.Select(r => r.Name).ToArray(),
            user.Locations.Select(l => l.Name).ToArray());

    public static UserListResponse ToListResponse(User user) =>
        new UserListResponse(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email.Value,
            user.IsActive,
            user.Roles.Select(r => r.Name).ToArray(),
            user.Locations.Select(l => l.Name).ToArray());
}
