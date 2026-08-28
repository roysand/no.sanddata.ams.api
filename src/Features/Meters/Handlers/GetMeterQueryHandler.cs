using Application.CQRS;
using Application.Common.Interfaces.Repositories;
using Domain.Common;
using Domain.Common.Entities;
using Features.Meters.Commands;
using Features.Meters.Mappers;
using Features.Meters.Queries;

namespace Features.Meters.Handlers;

public class GetMeterQueryHandler : IQueryHandler<GetMeterQuery, Result<MeterResponse>>
{
    private readonly IMeterEfRepository<Meter> _meterRepository;

    public GetMeterQueryHandler(IMeterEfRepository<Meter> meterRepository) => _meterRepository = meterRepository;

    public async Task<Result<MeterResponse>> Handle(GetMeterQuery query, CancellationToken ct)
    {
        Meter? meter = await _meterRepository.GetByIdAsync(query.Id, ct);
        if (meter is null)
        {
            return Result.Failure<MeterResponse>(Error.NotFound("Meter.NotFound", "Meter not found"));
        }

        return Result.Success(MeterMapper.ToResponse(meter));
    }
}
