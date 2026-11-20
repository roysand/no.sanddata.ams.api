using Application.Common.Interfaces.External;
using Application.CQRS;
using Application.EvChargers;
using Domain.Common;
using Features.EvChargers.Mappers;
using Features.EvChargers.Queries;

namespace Features.EvChargers.Handlers;

/// <summary>Asks Zaptec which chargers the server's sign-in can see, so an Admin can pick one without an id.</summary>
public class ListZaptecChargersQueryHandler(IEvChargerClient client)
    : IQueryHandler<ListZaptecChargersQuery, Result<IReadOnlyList<ZaptecChargerResponse>>>
{
    public async Task<Result<IReadOnlyList<ZaptecChargerResponse>>> Handle(ListZaptecChargersQuery query, CancellationToken ct)
    {
        Result<IReadOnlyList<ZaptecCharger>> chargers = await client.GetChargersAsync(ct);
        return chargers.IsFailure
            ? Result.Failure<IReadOnlyList<ZaptecChargerResponse>>(chargers.Error)
            : Result.Success<IReadOnlyList<ZaptecChargerResponse>>(
                chargers.Value.Select(EvChargerMapper.ToResponse).ToList());
    }
}
