using System.Globalization;
using Application.Common.Interfaces.Repositories;
using Application.ElectricityCost;
using Domain.Common;
using Domain.Common.Entities;
using Features.Tests.NorgesPris;
using NSubstitute;

namespace Features.Tests.Pricing;

/// <summary>
/// The calculator's public figures for spec 018: the Norgespris cap, the Norgespris price including VAT and the spot
/// energy price including VAT. Spot 100 EUR/MWh at 10 NOK per EUR is 1.00 NOK per kWh; VAT 25 percent.
/// </summary>
public class CostCalculatorStatusFiguresTests
{
    private static readonly DateTime Hour = Utc("2026-12-02T13:00:00Z");

    private readonly IElectricityPriceRepository<ElectricityPrice> _prices =
        Substitute.For<IElectricityPriceRepository<ElectricityPrice>>();
    private readonly IExchangeRateRepository<ExchangeRate> _fx = Substitute.For<IExchangeRateRepository<ExchangeRate>>();
    private decimal? _eurPerMwh = 100m;

    public CostCalculatorStatusFiguresTests()
    {
        _prices.GetByRegionAndHourAsync("NO1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => _eurPerMwh is { } eur
                ? new ElectricityPrice(Guid.NewGuid(), "NO1", call.ArgAt<DateTime>(1), eur)
                : null);
        _fx.GetByDateAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => new ExchangeRate(Guid.NewGuid(), "EURNOK", call.ArgAt<DateOnly>(1), 10m));
    }

    private static DateTime Utc(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private static Location Location(bool holidayHome = false, bool vatExempt = false)
    {
        var location = new Location(Guid.NewGuid(), "Cabin", "Addr", "SN1", "NO1", true, true);
        location.SetPricing(null, 0.05m, false, vatExempt, holidayHome);
        return location;
    }

    private CostCalculator Calculator(INorgesPrisRateRepository? norgesPris = null) => new(
        _prices,
        _fx,
        norgesPris ?? NorgesPrisRates.Seeded(),
        PriceRateStubs.Repository(
            PriceRateStubs.National2026()
                .Append(PriceRateStubs.Rate(PriceRateKind.NorgesPrisCapHolidayHome, "2025-10-01", 1000m))
                .Append(PriceRateStubs.Rate(PriceRateKind.NorgesPrisCapHome, "2025-10-01", 5000m))
                .Append(PriceRateStubs.Rate(PriceRateKind.NorgesPrisCapHome, "2027-01-01", 6000m)),
            []));

    [Theory]
    [InlineData(true, "2026-12-31T22:00:00Z", 1000)]
    [InlineData(false, "2026-12-31T22:00:00Z", 5000)]
    [InlineData(false, "2026-12-31T23:00:00Z", 6000)] // 00:00 Oslo on 1 January: the new month's limit
    public async Task TheCap_IsTheLimitOfTheLocationKindForTheOsloMonth(bool holidayHome, string instant, int expected) => Assert.Equal(expected, await Calculator().NorgesPrisCapAsync(
            Location(holidayHome), Utc(instant), CancellationToken.None));

    [Fact]
    public async Task TheNorgesPrisPrice_IsTheRateWithVat() => Assert.Equal(0.50m, await Calculator().NorgesPrisPriceWithVatAsync(Location(), Hour, CancellationToken.None));

    [Fact]
    public async Task TheNorgesPrisPrice_HasNoVatForAnExemptLocation() => Assert.Equal(0.40m, await Calculator().NorgesPrisPriceWithVatAsync(
            Location(vatExempt: true), Hour, CancellationToken.None));

    [Fact]
    public async Task TheNorgesPrisPrice_IsTheRateInEffectNow_NotOneStartingTomorrow()
    {
        CostCalculator calculator = Calculator(NorgesPrisRates.Repository(
            NorgesPrisRates.Rate("2020-01-01", 0.40m), NorgesPrisRates.Rate("2026-12-03", 0.48m)));

        Assert.Equal(0.50m, await calculator.NorgesPrisPriceWithVatAsync(Location(), Hour, CancellationToken.None));
        Assert.Equal(0.60m, await calculator.NorgesPrisPriceWithVatAsync(
            Location(), Utc("2026-12-02T23:00:00Z"), CancellationToken.None));
    }

    [Fact]
    public async Task TheSpotEnergyPrice_IsTheSpotPriceWithVatAndNothingElse()
    {
        Assert.Equal(1.25m, await Calculator().SpotEnergyWithVatAsync(Location(), Hour, CancellationToken.None));
        Assert.Equal(1.00m, await Calculator().SpotEnergyWithVatAsync(
            Location(vatExempt: true), Hour, CancellationToken.None));
    }

    [Fact]
    public async Task TheSpotEnergyPrice_StaysNegative()
    {
        _eurPerMwh = -20m;

        Assert.Equal(-0.25m, await Calculator().SpotEnergyWithVatAsync(Location(), Hour, CancellationToken.None));
    }

    [Fact]
    public async Task TheSpotEnergyPrice_IsNullWithoutAPrice()
    {
        _eurPerMwh = null;

        Assert.Null(await Calculator().SpotEnergyWithVatAsync(Location(), Hour, CancellationToken.None));
    }
}
