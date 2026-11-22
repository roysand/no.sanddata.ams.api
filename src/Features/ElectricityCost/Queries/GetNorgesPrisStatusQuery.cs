using Application.CQRS;
using Domain.Common;

namespace Features.ElectricityCost.Queries;

/// <summary>The Norgespris limit status of every active Norgespris location in the user's location list (spec 018).</summary>
public record GetNorgesPrisStatusQuery(Guid UserId) : IQuery<Result<NorgesPrisStatusResponse>>;

/// <param name="AsOf">The instant the figures were computed at.</param>
/// <param name="Items">One per listed location, in the order of the location list; empty when none.</param>
public record NorgesPrisStatusResponse(DateTime AsOf, IReadOnlyList<NorgesPrisStatusItem> Items);

/// <summary>One location's Norgespris limit and use in the current Oslo month. Figures are not rounded.</summary>
/// <param name="Month">The current Oslo month, yyyy-MM.</param>
/// <param name="LimitKwh">The month's Norgespris limit (holiday home or home); null when no limit is set.</param>
/// <param name="KwhSoFar">
/// The meter's kWh from the start of the month up to now, the running hour included, plus the car's when the location
/// counts it with the house.
/// </param>
/// <param name="ProjectedKwh">
/// The bill's projected total for the month (its totalKwh + projection.kwh); null without readings in the last 7 days.
/// </param>
/// <param name="OverLimit">True when <paramref name="KwhSoFar"/> is at or above <paramref name="LimitKwh"/>.</param>
/// <param name="OverLimitKwh">By how many kWh it is over; 0 when not over.</param>
/// <param name="NorgesPrisPricePerKwh">
/// The Norgespris rate in effect now including VAT, without grid charge, taxes or markup; null when not set.
/// </param>
/// <param name="ResetsAt">00:00 Oslo time on the 1st of next month, in UTC.</param>
public record NorgesPrisStatusItem(
    Guid LocationId,
    string Month,
    decimal? LimitKwh,
    decimal KwhSoFar,
    decimal? ProjectedKwh,
    bool OverLimit,
    decimal OverLimitKwh,
    decimal? NorgesPrisPricePerKwh,
    DateTime ResetsAt);
