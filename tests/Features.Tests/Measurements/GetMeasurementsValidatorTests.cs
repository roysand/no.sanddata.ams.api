using Features.Measurements.Endpoints;
using Features.Measurements.Validators;
using FluentValidation.Results;

namespace Features.Tests.Measurements;

public class GetMeasurementsValidatorTests
{
    private readonly GetMeasurementsValidator _validator = new();

    [Fact]
    public void Validate_DefaultRequest_Passes()
    {
        var request = new GetMeasurementsRequest(Guid.NewGuid());

        ValidationResult result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_PageBelowOne_Fails(int page)
    {
        var request = new GetMeasurementsRequest(Guid.NewGuid(), Page: page);

        ValidationResult result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetMeasurementsRequest.Page));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2001)]
    public void Validate_PageSizeOutOfBounds_Fails(int pageSize)
    {
        var request = new GetMeasurementsRequest(Guid.NewGuid(), PageSize: pageSize);

        ValidationResult result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GetMeasurementsRequest.PageSize));
    }

    [Fact]
    public void Validate_ToBeforeFrom_FailsWithInvalidRangeCode()
    {
        DateTime now = DateTime.UtcNow;
        var request = new GetMeasurementsRequest(Guid.NewGuid(), From: now, To: now.AddHours(-1));

        ValidationResult result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "Validation.InvalidRange");
    }

    [Fact]
    public void Validate_ToEqualsFrom_Passes()
    {
        DateTime now = DateTime.UtcNow;
        var request = new GetMeasurementsRequest(Guid.NewGuid(), From: now, To: now);

        ValidationResult result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_OnlyFromProvided_Passes()
    {
        var request = new GetMeasurementsRequest(Guid.NewGuid(), From: DateTime.UtcNow);

        ValidationResult result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }
}
