using FastEndpoints;
using Features.Measurements.Endpoints;
using FluentValidation;

namespace Features.Measurements.Validators;

public class IngestMeasurementsValidator : Validator<IngestMeasurementsRequest>
{
    public IngestMeasurementsValidator()
    {
        RuleFor(x => x.DeviceId)
            .NotEmpty().WithMessage("Device id is required")
            .MaximumLength(100).WithMessage("Device id must not exceed 100 characters");

        RuleFor(x => x.Readings)
            .NotEmpty().WithMessage("At least one reading is required");

        RuleForEach(x => x.Readings).ChildRules(reading =>
        {
            reading.RuleFor(r => r.Timestamp)
                .GreaterThan(0).WithMessage("Timestamp is required");

            reading.RuleFor(r => r.PowerWatts)
                .InclusiveBetween(-50_000, 50_000).WithMessage("Power watts is outside the expected range");
        });
    }
}
