using Application.CQRS;
using Domain.Common;

namespace Features.Users.Commands;

public record DeleteUserCommand(Guid Id) : ICommand<Result<DeleteUserResponse>>;

public record DeleteUserResponse(bool Success);
