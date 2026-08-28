using Application.CQRS;
using Domain.Common;
using Features.Meters.Commands;

namespace Features.Meters.Queries;

public record GetMeterQuery(Guid Id) : IQuery<Result<MeterResponse>>;
