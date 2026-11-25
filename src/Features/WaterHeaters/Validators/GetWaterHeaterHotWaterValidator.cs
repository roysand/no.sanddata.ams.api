using System.Globalization;
using FastEndpoints;
using Features.WaterHeaters.Endpoints;
using Features.WaterHeaters.Queries;
using FluentValidation;

namespace Features.WaterHeaters.Validators;

/// <summary>
/// The hot water view's period and date (spec 020, research R7). Both may be left out; given, the period must be one
/// of the four and the date a real <c>yyyy-MM-dd</c> date.
/// </summary>
public class GetWaterHeaterHotWaterValidator : Validator<GetWaterHeaterHotWaterRequest>
{
    public GetWaterHeaterHotWaterValidator()
    {
        RuleFor(r => r.LocationId).NotEmpty();

        RuleFor(r => r.Period)
            .Must(p => Enum.GetNames<HotWaterPeriod>().Contains(p, StringComparer.OrdinalIgnoreCase))
            .When(r => r.Period is not null)
            .WithMessage("period must be day, week, month or year")
            .WithErrorCode("Validation.InvalidPeriod");

        RuleFor(r => r.Date)
            .Must(d => DateOnly.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .When(r => r.Date is not null)
            .WithMessage("date must be a real date as yyyy-MM-dd")
            .WithErrorCode("Validation.InvalidDate");
    }
}
