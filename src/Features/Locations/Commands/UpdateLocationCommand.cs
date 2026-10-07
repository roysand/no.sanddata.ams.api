using Application.CQRS;
using Domain.Common;
using Features.Locations.Queries;

namespace Features.Locations.Commands;

public record UpdateLocationCommand(
    Guid ActingUserId,
    Guid LocationId,
    string Name,
    string Address,
    string SerialNumber,
    string Zone,
    bool HasNorgesPriceAgreement,
    bool IsActive) : ICommand<Result<AdminLocationResponse>>;
