using Application.Common.Interfaces.Repositories;
using Application.ElectricityCost;
using Domain.Common.Entities;
using NSubstitute;

namespace Features.Tests.NorgesPris;

public class CostCalculatorNorgesPrisTests
{
    private static readonly DateTime Before = new(2026, 12, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime After = new(2027, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private readonly IElectricityPriceRepository<ElectricityPrice> _prices =
        Substitute.For<IElectricityPriceRepository<ElectricityPrice>>();

    private readonly IExchangeRateRepository<ExchangeRate> _exchangeRates =
        Substitute.For<IExchangeRateRepository<ExchangeRate>>();

    private static Location LocationWith(bool norgesPris) =>
        new(Guid.NewGuid(), "Home", "Addr", "SN1", "NO1", true, norgesPris);

    private CostCalculator Calculator(INorgesPrisRateRepository rates) => new(_prices, _exchangeRates, rates);

    private INorgesPrisRateRepository TwoRates() => NorgesPrisRates.Repository(
        NorgesPrisRates.Rate("2020-01-01", 0.40m, 0.05m),
        NorgesPrisRates.Rate("2027-01-01", 0.50m, 0.02m));

    [Fact]
    public async Task Calculate_NorgesPrisLocation_UsesTheRateValidForEachHour()
    {
        CostCalculator calculator = Calculator(TwoRates());

        CostResult before = await calculator.CalculateAsync(LocationWith(true), Before, 2m, CancellationToken.None);
        CostResult after = await calculator.CalculateAsync(LocationWith(true), After, 2m, CancellationToken.None);

        Assert.Equal(0.45m, before.Actual!.RatePerKwh); // rate plus tax
        Assert.Equal(0.90m, before.Actual.Cost);
        Assert.Equal(0.52m, after.Actual!.RatePerKwh);
        Assert.Equal(1.04m, after.Actual.Cost);
    }

    [Fact]
    public async Task Calculate_SpotLocation_ComparisonFollowsTheSameRule()
    {
        CostCalculator calculator = Calculator(TwoRates());

        CostResult before = await calculator.CalculateAsync(LocationWith(false), Before, 1m, CancellationToken.None);
        CostResult after = await calculator.CalculateAsync(LocationWith(false), After, 1m, CancellationToken.None);

        Assert.Equal(0.45m, before.Comparison!.RatePerKwh);
        Assert.Equal(0.52m, after.Comparison!.RatePerKwh);
    }

    [Fact]
    public async Task Calculate_HourBeforeTheEarliestRate_IsUnavailableNotZero()
    {
        CostCalculator calculator = Calculator(TwoRates());

        CostResult result = await calculator.CalculateAsync(
            LocationWith(true), new DateTime(2019, 6, 1, 10, 0, 0, DateTimeKind.Utc), 1m, CancellationToken.None);

        Assert.Null(result.Actual);
    }

    [Fact]
    public async Task Calculate_ManyHours_LoadsTheRatesOnce()
    {
        INorgesPrisRateRepository rates = TwoRates();
        CostCalculator calculator = Calculator(rates);
        Location location = LocationWith(true);

        for (int hour = 0; hour < 24 * 30; hour++)
        {
            await calculator.CalculateAsync(location, Before.AddHours(hour), 1m, CancellationToken.None);
        }

        await rates.Received(1).GetAllAsync(Arg.Any<CancellationToken>());
    }
}
