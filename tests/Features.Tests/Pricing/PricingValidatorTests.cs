using Features.Pricing.Endpoints;
using Features.Pricing.Validators;

namespace Features.Tests.Pricing;

public class PricingValidatorTests
{
    private static readonly DateOnly Date = new(2027, 1, 1);

    // ---- national rates ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("ElectricityTax")]
    [InlineData("EnovaFee")]
    [InlineData("Vat")]
    [InlineData("vat")]
    [InlineData("ENOVAFEE")]
    public void AddPriceRate_AKnownKindInAnyCase_Passes(string kind) =>
        Assert.True(new AddPriceRateValidator().Validate(new AddPriceRateRequest(kind, Date, 0.05m)).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("Tax")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("99")]
    public void AddPriceRate_AnUnknownKindOrANumber_IsRefused(string kind) =>
        Assert.False(new AddPriceRateValidator().Validate(new AddPriceRateRequest(kind, Date, 0.05m)).IsValid);

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.0001)]
    [InlineData(0.07131)]
    public void AddPriceRate_AFeeThatIsNegativeTooBigOrHasTooManyDecimals_IsRefused(double value) =>
        Assert.False(
            new AddPriceRateValidator().Validate(new AddPriceRateRequest("ElectricityTax", Date, (decimal)value)).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(0.0713)]
    public void AddPriceRate_TheBoundaryFees_Pass(double value) =>
        Assert.True(
            new AddPriceRateValidator().Validate(new AddPriceRateRequest("EnovaFee", Date, (decimal)value)).IsValid);

    [Fact]
    public void AddPriceRate_ACapIsKwhPerMonth_AboveZeroAndAtMost99999()
    {
        foreach (string kind in new[] { "NorgesPrisCapHome", "NorgesPrisCapHolidayHome" })
        {
            Assert.True(new AddPriceRateValidator().Validate(new AddPriceRateRequest(kind, Date, 1000m)).IsValid);
            Assert.True(new AddPriceRateValidator().Validate(new AddPriceRateRequest(kind, Date, 99999m)).IsValid);
            Assert.False(new AddPriceRateValidator().Validate(new AddPriceRateRequest(kind, Date, 0m)).IsValid);
            Assert.False(new AddPriceRateValidator().Validate(new AddPriceRateRequest(kind, Date, 100000m)).IsValid);
            Assert.False(new AddPriceRateValidator().Validate(new AddPriceRateRequest(kind, Date, 1000.00001m)).IsValid);
        }

        // A fee keeps its limit of 100 NOK per kWh.
        Assert.False(new AddPriceRateValidator().Validate(new AddPriceRateRequest("EnovaFee", Date, 1000m)).IsValid);
    }

    [Fact]
    public void AddPriceRate_VatIsAFraction_SoAboveOneIsRefused()
    {
        Assert.True(new AddPriceRateValidator().Validate(new AddPriceRateRequest("Vat", Date, 0.25m)).IsValid);
        Assert.True(new AddPriceRateValidator().Validate(new AddPriceRateRequest("Vat", Date, 1m)).IsValid);
        Assert.False(new AddPriceRateValidator().Validate(new AddPriceRateRequest("Vat", Date, 25m)).IsValid);
        Assert.False(new AddPriceRateValidator().Validate(new AddPriceRateRequest("Vat", Date, 1.0001m)).IsValid);
    }

    [Fact]
    public void AddPriceRate_NoStartDate_IsRefused() =>
        Assert.False(
            new AddPriceRateValidator().Validate(new AddPriceRateRequest("Vat", default, 0.25m)).IsValid);

    [Fact]
    public void ListPriceRates_NoKindOrAKnownKind_Passes_AnUnknownKindIsRefused()
    {
        var validator = new ListPriceRatesValidator();

        Assert.True(validator.Validate(new ListPriceRatesRequest()).IsValid);
        Assert.True(validator.Validate(new ListPriceRatesRequest("vat")).IsValid);
        Assert.False(validator.Validate(new ListPriceRatesRequest("Tax")).IsValid);
        Assert.False(validator.Validate(new ListPriceRatesRequest("1")).IsValid);
    }

    // ---- grid tariffs -----------------------------------------------------------------------------------------

    [Fact]
    public void CreateGridTariff_TheNameLimits()
    {
        var validator = new CreateGridTariffValidator();

        Assert.True(validator.Validate(new CreateGridTariffRequest("Elvia household")).IsValid);
        Assert.True(validator.Validate(new CreateGridTariffRequest(new string('a', 100))).IsValid);
        Assert.False(validator.Validate(new CreateGridTariffRequest("")).IsValid);
        Assert.False(validator.Validate(new CreateGridTariffRequest("   ")).IsValid);
        Assert.False(validator.Validate(new CreateGridTariffRequest(new string('a', 101))).IsValid);
    }

    [Theory]
    [InlineData(-0.01, 0.2)]
    [InlineData(0.3, -0.01)]
    [InlineData(100.0001, 0.2)]
    [InlineData(0.3, 100.0001)]
    [InlineData(0.30001, 0.2)]
    [InlineData(0.3, 0.20001)]
    public void AddGridTariffRate_ARateThatIsNegativeTooBigOrHasTooManyDecimals_IsRefused(double day, double night) =>
        Assert.False(
            new AddGridTariffRateValidator()
                .Validate(new AddGridTariffRateRequest(Guid.NewGuid(), Date, (decimal)day, (decimal)night)).IsValid);

    [Fact]
    public void AddGridTariffRate_ValidRatesAndNoDate()
    {
        var validator = new AddGridTariffRateValidator();

        Assert.True(validator.Validate(new AddGridTariffRateRequest(Guid.NewGuid(), Date, 0.3641m, 0.2641m)).IsValid);
        Assert.True(validator.Validate(new AddGridTariffRateRequest(Guid.NewGuid(), Date, 0m, 100m)).IsValid);
        Assert.False(validator.Validate(new AddGridTariffRateRequest(Guid.NewGuid(), default, 0.3m, 0.2m)).IsValid);
    }

    // ---- location pricing -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, true)]
    [InlineData(0.0250, true)]
    [InlineData(100, true)]
    [InlineData(-0.01, false)]
    [InlineData(100.0001, false)]
    [InlineData(0.02501, false)]
    public void SetLocationPricing_TheMarkupLimits(double markup, bool valid) =>
        Assert.Equal(
            valid,
            new SetLocationPricingValidator()
                .Validate(new SetLocationPricingRequest(Guid.NewGuid(), null, (decimal)markup)).IsValid);

    [Theory]
    [InlineData(null, true)]
    [InlineData(0d, true)]
    [InlineData(27.20, true)]
    [InlineData(10000d, true)]
    [InlineData(-0.01, false)]
    [InlineData(10000.01, false)]
    [InlineData(27.20001, false)]
    public void SetLocationPricing_TheMonthlyFeeLimits(double? fee, bool valid)
    {
        FluentValidation.Results.ValidationResult result = new SetLocationPricingValidator().Validate(
            new SetLocationPricingRequest(Guid.NewGuid(), null, 0m, SupplierMonthlyFee: (decimal?)fee));

        Assert.Equal(valid, result.IsValid);
        if (!valid)
        {
            Assert.Equal("Validation.InvalidMonthlyFee", Assert.Single(result.Errors).ErrorCode);
        }
    }
}
