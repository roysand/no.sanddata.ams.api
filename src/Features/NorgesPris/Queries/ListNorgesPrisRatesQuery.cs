using Application.CQRS;
using Domain.Common;

namespace Features.NorgesPris.Queries;

public record ListNorgesPrisRatesQuery(Guid ActingUserId) : IQuery<Result<IReadOnlyList<NorgesPrisRateResponse>>>;

/// <param name="Status">Superseded, InEffect (exactly one) or Upcoming, from today's date in Norway.</param>
public record NorgesPrisRateResponse(
    Guid Id, DateOnly ValidFrom, decimal RatePerKwh, decimal TaxPerKwh, decimal TotalPerKwh, string Status);
