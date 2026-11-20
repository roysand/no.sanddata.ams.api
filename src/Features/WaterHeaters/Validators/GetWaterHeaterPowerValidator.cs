using FastEndpoints;
using Features.WaterHeaters.Endpoints;
using Features.WaterHeaters.Services;
using FluentValidation;

namespace Features.WaterHeaters.Validators;

/// <summary>
/// The power window: from before to, at most 48 hours, from not in the future (spec 017, FR-004). The defaults are
/// filled in first, with the same <see cref="PowerWindow"/> the handler uses (research R3).
/// </summary>
public class GetWaterHeaterPowerValidator : Validator<GetWaterHeaterPowerRequest>
{
    public GetWaterHeaterPowerValidator()
    {
        RuleFor(r => r.LocationId).NotEmpty();
        RuleFor(r => r).Custom((r, ctx) =>
        {
            DateTime now = Resolve<TimeProvider>().GetUtcNow().UtcDateTime;
            (DateTime from, DateTime to) = PowerWindow.Resolve(r.From, r.To, now);
            if (PowerWindow.Problem(from, to, now) is { } problem)
            {
                ctx.AddFailure(new FluentValidation.Results.ValidationFailure("window", problem)
                {
                    ErrorCode = "Validation.InvalidWindow"
                });
            }
        });
    }
}
