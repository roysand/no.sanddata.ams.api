using FastEndpoints;
using Features.EvChargers.Endpoints;
using FluentValidation;

namespace Features.EvChargers.Validators;

/// <summary>Shape only; whether Zaptec knows the charger is decided by the handler.</summary>
public class ConnectEvChargerValidator : Validator<ConnectEvChargerRequest>
{
    public ConnectEvChargerValidator()
    {
        RuleFor(r => r.LocationId).NotEmpty();
        RuleFor(r => r.ChargerId).NotEmpty().WithMessage("The chargerId is required");
    }
}
