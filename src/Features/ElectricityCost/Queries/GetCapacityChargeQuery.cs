using Application.CQRS;
using Domain.Common;

namespace Features.ElectricityCost.Queries;

/// <param name="Month">A day in the Oslo calendar month, or null for the current month.</param>
public record GetCapacityChargeQuery(Guid UserId, Guid LocationId, DateOnly? Month)
    : IQuery<Result<CapacityChargeResponse>>;

/// <param name="Kw">The kWh of the highest hour of the day, which is its average kW.</param>
public record CapacityPeakResponse(DateOnly Date, DateTime HourStartUtc, decimal Kw);

/// <param name="UpperKw">Null for the top step.</param>
public record CapacityStepRangeResponse(decimal LowerKw, decimal? UpperKw);

/// <param name="Month">yyyy-MM.</param>
/// <param name="Provisional">True until the month has ended: the step can still go up.</param>
/// <param name="Set">
/// False when the location has no grid tariff, the tariff has no step table for the month, or there is no consumption;
/// the figures are then null, never zero.
/// </param>
/// <param name="Peaks">The daily peaks averaged, highest first: three on different days, or fewer days with data.</param>
/// <param name="AmountExclVat">The monthly amount of the step, NOK excluding VAT.</param>
/// <param name="Amount">Including VAT (none for a VAT-exempt location).</param>
/// <param name="KwToNextStep">How far the average is below the next step; null on the top step.</param>
public record CapacityChargeResponse(
    string Month,
    bool Provisional,
    bool Set,
    IReadOnlyList<CapacityPeakResponse> Peaks,
    decimal? AverageKw,
    CapacityStepRangeResponse? Step,
    decimal? AmountExclVat,
    decimal? Vat,
    decimal? Amount,
    decimal? KwToNextStep);
