using Application.Common.Interfaces.Repositories;
using Application.CQRS;
using Application.ElectricityCost;
using Domain.Common;
using Domain.Common.Entities;
using Features.NorgesPris.Logging;
using Features.NorgesPris.Mappers;
using Features.NorgesPris.Queries;
using Microsoft.Extensions.Logging;

namespace Features.NorgesPris.Handlers;

public class ListNorgesPrisRatesQueryHandler(
    INorgesPrisRateRepository rates,
    TimeProvider timeProvider,
    ILogger<ListNorgesPrisRatesQueryHandler> logger)
    : IQueryHandler<ListNorgesPrisRatesQuery, Result<IReadOnlyList<NorgesPrisRateResponse>>>
{
    public async Task<Result<IReadOnlyList<NorgesPrisRateResponse>>> Handle(
        ListNorgesPrisRatesQuery query, CancellationToken ct)
    {
        IReadOnlyList<NorgesPrisRate> all = await rates.GetAllAsync(ct);
        DateOnly today = NorgesPrisSchedule.OsloDateOf(timeProvider.GetUtcNow().UtcDateTime);

        LogMessages.RatesListed(logger, query.ActingUserId, all.Count);

        return Result.Success(NorgesPrisMapper.ToResponses(all, today));
    }
}
