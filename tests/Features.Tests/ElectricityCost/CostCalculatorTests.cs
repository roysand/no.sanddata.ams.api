using Application.Common.Interfaces.Repositories;
using Application.ElectricityCost;
using Domain.Common.Entities;
using NSubstitute;

namespace Features.Tests.ElectricityCost;

public class CostCalculatorTests
{
    private static readonly DateTime Hour = new(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc);

    private readonly IElectricityPriceRepository<ElectricityPrice> _prices =
        Substitute.For<IElectricityPriceRepository<ElectricityPrice>>();

    private readonly IExchangeRateRepository<ExchangeRate> _rates =
        Substitute.For<IExchangeRateRepository<ExchangeRate>>();

    private readonly CostCalculator _calculator;

    public CostCalculatorTests() => _calculator = new CostCalculator(_prices, _rates, new FlatRateOptions { RatePerKwh = 0.40m, TaxPerKwh = 0.05m });

    private static Location LocationWith(bool norgesPris) =>
        new(Guid.NewGuid(), "Home", "Addr", "SN1", "NO1", true, norgesPris);

    private void GivenSpotPrice(decimal eurPerMwh) =>
        _prices.GetByRegionAndHourAsync("NO1", Hour, Arg.Any<CancellationToken>())
            .Returns(new ElectricityPrice(Guid.NewGuid(), "NO1", Hour, eurPerMwh));

    private void GivenRate(DateOnly date, decimal rate) =>
        _rates.GetByDateAsync("EURNOK", date, Arg.Any<CancellationToken>())
            .Returns(new ExchangeRate(Guid.NewGuid(), "EURNOK", date, rate));

    [Fact]
    public async Task Calculate_SpotLocation_UsesPriceTimesExchangeRate()
    {
        GivenSpotPrice(100m); // 100 EUR/MWh = 0.1 EUR/kWh
        GivenRate(DateOnly.FromDateTime(Hour), 11m);

        CostResult result = await _calculator.CalculateAsync(LocationWith(false), Hour, 2m, CancellationToken.None);

        Assert.Equal(PricingModel.Spot, result.PricingModel);
        Assert.Equal(1.1m, result.Actual!.RatePerKwh);
        Assert.Equal(2.2m, result.Actual.Cost);
    }

    [Fact]
    public async Task Calculate_SpotLocation_ComparisonIsFlatRatePlusTax()
    {
        GivenSpotPrice(100m);
        GivenRate(DateOnly.FromDateTime(Hour), 11m);

        CostResult result = await _calculator.CalculateAsync(LocationWith(false), Hour, 2m, CancellationToken.None);

        Assert.Equal(0.45m, result.Comparison!.RatePerKwh);
        Assert.Equal(0.90m, result.Comparison.Cost);
    }

    [Fact]
    public async Task Calculate_NorgesPrisLocation_ActualIsFlatAndComparisonIsSpot()
    {
        GivenSpotPrice(100m);
        GivenRate(DateOnly.FromDateTime(Hour), 11m);

        CostResult result = await _calculator.CalculateAsync(LocationWith(true), Hour, 2m, CancellationToken.None);

        Assert.Equal(PricingModel.NorgesPris, result.PricingModel);
        Assert.Equal(0.45m, result.Actual!.RatePerKwh);
        Assert.Equal(1.1m, result.Comparison!.RatePerKwh);
    }

    [Fact]
    public async Task Calculate_NoSpotPrice_SpotCostIsNullButFlatStillWorks()
    {
        CostResult result = await _calculator.CalculateAsync(LocationWith(false), Hour, 2m, CancellationToken.None);

        Assert.Null(result.Actual);
        Assert.NotNull(result.Comparison);
    }

    [Fact]
    public async Task Calculate_NoExchangeRateWithinLookback_SpotCostIsNull()
    {
        GivenSpotPrice(100m);

        CostResult result = await _calculator.CalculateAsync(LocationWith(false), Hour, 2m, CancellationToken.None);

        Assert.Null(result.Actual);
    }

    [Fact]
    public async Task Calculate_NoRateOnDay_FallsBackToMostRecentEarlierRate()
    {
        GivenSpotPrice(100m);
        GivenRate(DateOnly.FromDateTime(Hour).AddDays(-2), 10m); // e.g. weekend, last business day

        CostResult result = await _calculator.CalculateAsync(LocationWith(false), Hour, 1m, CancellationToken.None);

        Assert.Equal(1.0m, result.Actual!.RatePerKwh);
    }

    [Fact]
    public async Task Calculate_ZeroConsumption_CostIsZero()
    {
        GivenSpotPrice(100m);
        GivenRate(DateOnly.FromDateTime(Hour), 11m);

        CostResult result = await _calculator.CalculateAsync(LocationWith(false), Hour, 0m, CancellationToken.None);

        Assert.Equal(0m, result.Actual!.Cost);
    }
}
