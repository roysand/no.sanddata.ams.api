using FastEndpoints;
using Features.WaterHeaters.Endpoints;
using FluentValidation;

namespace Features.WaterHeaters.Validators;

/// <summary>Shape only; whether OSO accepts the pair is decided by the handler. Messages never echo the values.</summary>
public class ConnectWaterHeaterValidator : Validator<ConnectWaterHeaterRequest>
{
    private const int MaxLength = 200;

    public ConnectWaterHeaterValidator()
    {
        RuleFor(r => r.LocationId).NotEmpty();

        RuleFor(r => r.SubscriptionKey)
            .NotEmpty().WithMessage("The subscription key is required")
            .MaximumLength(MaxLength).WithMessage($"The subscription key can be at most {MaxLength} characters")
            .Must(k => k is null || !k.Any(char.IsWhiteSpace)).WithMessage("The subscription key cannot contain spaces");

        RuleFor(r => r.DeviceId)
            .NotEmpty().WithMessage("The device id is required")
            .MaximumLength(MaxLength).WithMessage($"The device id can be at most {MaxLength} characters")
            .Matches("^[0-9a-fA-F]+$").WithMessage("The device id must be hexadecimal");
    }
}
