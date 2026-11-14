using Application.CQRS;
using Domain.Common;
using Features.NorgesPris.Queries;

namespace Features.NorgesPris.Commands;

/// <param name="ConfirmPastDate">
/// Must be true when ValidFrom is today or earlier: the caller has accepted that costs shown for past hours
/// will be recalculated.
/// </param>
public record AddNorgesPrisRateCommand(
    DateOnly ValidFrom, decimal RatePerKwh, decimal TaxPerKwh, bool ConfirmPastDate, Guid ActingUserId)
    : ICommand<Result<NorgesPrisRateResponse>>;

public record DeleteNorgesPrisRateCommand(Guid Id, Guid ActingUserId)
    : ICommand<Result<NorgesPrisRateDeleted>>;

public record NorgesPrisRateDeleted(Guid Id);
