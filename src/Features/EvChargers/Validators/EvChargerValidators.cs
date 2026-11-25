using FastEndpoints;
using Features.EvChargers.Endpoints;
using FluentValidation;

namespace Features.EvChargers.Validators;

/// <summary>The sessions range: from before to, at most 366 days (spec 016, contracts).</summary>
public class GetChargingSessionsValidator : Validator<GetChargingSessionsRequest>
{
    public static readonly TimeSpan MaxRange = TimeSpan.FromDays(366);

    public GetChargingSessionsValidator()
    {
        RuleFor(r => r.LocationId).NotEmpty();
        RuleFor(r => r)
            .Must(r => r.From is not { } from || r.To is not { } to || from.ToUniversalTime() < to.ToUniversalTime())
            .WithMessage("from must be before to")
            .Must(r => r.From is not { } from || r.To is not { } to
                       || to.ToUniversalTime() - from.ToUniversalTime() <= MaxRange)
            .WithMessage("The range can be at most 366 days");
    }
}

/// <summary>The charger's state right now (spec 021): only the route's location id.</summary>
public class GetChargerStateValidator : Validator<GetChargerStateRequest>
{
    public GetChargerStateValidator() => RuleFor(r => r.LocationId).NotEmpty();
}

public class SetIncludeEvChargerValidator : Validator<SetIncludeEvChargerRequest>
{
    public SetIncludeEvChargerValidator()
    {
        RuleFor(r => r.LocationId).NotEmpty();
        RuleFor(r => r.Include).NotNull().WithMessage("include is required (true or false)");
    }
}
