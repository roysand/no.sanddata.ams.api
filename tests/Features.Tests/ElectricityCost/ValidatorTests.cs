using Features.ElectricityCost.Endpoints;
using Features.ElectricityCost.Validators;

namespace Features.Tests.ElectricityCost;

public class ValidatorTests
{
    private static readonly DateTime Earlier = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Earlier.AddDays(1);

    [Fact]
    public void Hourly_DefaultRequest_Passes() =>
        Assert.True(new GetHourlyCostValidator().Validate(new GetHourlyCostRequest(Guid.NewGuid())).IsValid);

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 2001)]
    public void Hourly_PagingOutOfBounds_Fails(int page, int pageSize) =>
        Assert.False(new GetHourlyCostValidator()
            .Validate(new GetHourlyCostRequest(Guid.NewGuid(), Page: page, PageSize: pageSize)).IsValid);

    [Fact]
    public void Hourly_ToBeforeFrom_Fails() =>
        Assert.False(new GetHourlyCostValidator()
            .Validate(new GetHourlyCostRequest(Guid.NewGuid(), Later, Earlier)).IsValid);

    [Fact]
    public void Daily_ToBeforeFrom_Fails() =>
        Assert.False(new GetDailyCostValidator().Validate(new GetDailyCostRequest(Guid.NewGuid(), Later, Earlier)).IsValid);

    [Fact]
    public void Daily_ValidRange_Passes() =>
        Assert.True(new GetDailyCostValidator().Validate(new GetDailyCostRequest(Guid.NewGuid(), Earlier, Later)).IsValid);

    [Theory]
    [InlineData("minute")]
    [InlineData("hour")]
    public void Consumption_SupportedGranularity_Passes(string granularity) =>
        Assert.True(new GetConsumptionValidator()
            .Validate(new GetConsumptionRequest(Guid.NewGuid(), granularity)).IsValid);

    [Theory]
    [InlineData("day")]
    [InlineData("")]
    [InlineData("Hour")]
    public void Consumption_UnsupportedGranularity_Fails(string granularity) =>
        Assert.False(new GetConsumptionValidator()
            .Validate(new GetConsumptionRequest(Guid.NewGuid(), granularity)).IsValid);

    [Fact]
    public void Consumption_ToBeforeFrom_Fails() =>
        Assert.False(new GetConsumptionValidator()
            .Validate(new GetConsumptionRequest(Guid.NewGuid(), "hour", From: Later, To: Earlier)).IsValid);
}
