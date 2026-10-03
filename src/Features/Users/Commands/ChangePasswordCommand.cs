using Application.CQRS;
using Domain.Common;

namespace Features.Users.Commands;

public record ChangePasswordCommand(
    Guid Id,
    string? CurrentPassword,
    string NewPassword,
    Caller Caller
) : ICommand<Result<ChangePasswordResponse>>;

public record ChangePasswordResponse(bool Success);
