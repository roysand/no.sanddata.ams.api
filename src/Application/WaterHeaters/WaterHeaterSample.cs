using Domain.Common;

namespace Application.WaterHeaters;

/// <summary>
/// What one sampler call reads from OSO (spec 020, research R2): the power and the litres of 40 °C water in the tank.
/// <see cref="Litres40"/> is null when OSO's status could not be read or had no usable number, and
/// <see cref="LitresError"/> then says why. The power is always there: without it the whole call fails.
/// </summary>
public sealed record WaterHeaterSample(decimal PowerKw, int? Litres40, Error? LitresError);
