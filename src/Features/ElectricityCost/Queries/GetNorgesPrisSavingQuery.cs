using Application.CQRS;
using Domain.Common;

namespace Features.ElectricityCost.Queries;

public record GetNorgesPrisSavingQuery(Guid UserId, Guid LocationId) : IQuery<Result<NorgesPrisSavingResponse>>;

/// <param name="Period">Today, Last7Days, MonthToDate or YearToDate (Oslo calendar days).</param>
/// <param name="Saving">
/// Cost on spot terms minus cost on Norgespris terms over the counted hours, in NOK including VAT. Negative means
/// Norgespris cost more than spot would have.
/// </param>
/// <param name="HoursCounted">Hours where both sides could be priced.</param>
/// <param name="HoursWithConsumption">Hours with consumption in the period; at least HoursCounted.</param>
public record SavingPeriodResponse(
    string Period, DateOnly From, DateOnly To, decimal Saving, int HoursCounted, int HoursWithConsumption);

public record NorgesPrisSavingResponse(IReadOnlyList<SavingPeriodResponse> Periods);
