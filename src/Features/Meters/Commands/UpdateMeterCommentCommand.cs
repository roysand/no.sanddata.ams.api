using Application.CQRS;
using Domain.Common;

namespace Features.Meters.Commands;

/// <summary>Changes the comment of a registered reader; nothing else about it can change.</summary>
public record UpdateMeterCommentCommand(Guid MeterId, Guid UserId, bool IsAdmin, string? Comment)
    : ICommand<Result<MeterResponse>>;
