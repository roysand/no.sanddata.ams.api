using Features.NorgesPris.Endpoints;
using Features.NorgesPris.Validators;

namespace Features.Tests.NorgesPris;

public class AddNorgesPrisRateValidatorTests
{
    private readonly AddNorgesPrisRateValidator _validator = new();

    private static AddNorgesPrisRateRequest Request(
        DateOnly? validFrom = null, decimal rate = 0.50m, decimal tax = 0m) =>
        new(validFrom ?? new DateOnly(2027, 1, 1), rate, tax);

    [Fact]
    public void Validate_AValidRequest_Passes() =>
        Assert.True(_validator.Validate(Request()).IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(0.4567)]
    public void Validate_BoundaryAmounts_Pass(double amount) => Assert.True(_validator.Validate(Request(rate: (decimal)amount, tax: (decimal)amount)).IsValid);

    [Fact]
    public void Validate_ANegativeRate_IsRejected() =>
        Assert.False(_validator.Validate(Request(rate: -0.01m)).IsValid);

    [Fact]
    public void Validate_ANegativeTax_IsRejected() =>
        Assert.False(_validator.Validate(Request(tax: -0.01m)).IsValid);

    [Fact]
    public void Validate_MoreThanFourDecimals_IsRejected()
    {
        Assert.False(_validator.Validate(Request(rate: 0.12345m)).IsValid);
        Assert.False(_validator.Validate(Request(tax: 0.00001m)).IsValid);
    }

    [Fact]
    public void Validate_AnAmountAboveTheLimit_IsRejected() =>
        Assert.False(_validator.Validate(Request(rate: 100.0001m)).IsValid);

    [Fact]
    public void Validate_NoStartDate_IsRejected() =>
        Assert.False(_validator.Validate(Request(validFrom: default(DateOnly))).IsValid);
}
