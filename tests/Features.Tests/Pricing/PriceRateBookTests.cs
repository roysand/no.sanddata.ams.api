using System.Globalization;
using Application.ElectricityCost;
using Domain.Common;
using Domain.Common.Entities;

namespace Features.Tests.Pricing;

public class PriceRateBookTests
{
    private static readonly Guid TariffA = Guid.NewGuid();
    private static readonly Guid TariffB = Guid.NewGuid();

    private static DateTime Utc(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private static PriceRate Rate(PriceRateKind kind, string validFrom, decimal value) =>
        new(Guid.NewGuid(), kind, DateOnly.Parse(validFrom), value, Guid.Empty);

    private static GridTariffRate Grid(Guid tariff, string validFrom, decimal day, decimal night) =>
        new(Guid.NewGuid(), tariff, DateOnly.Parse(validFrom), day, night, Guid.Empty);

    private static PriceRateBook Book() => new(
        [
            Rate(PriceRateKind.Vat, "2020-01-01", 0.25m),
            Rate(PriceRateKind.ElectricityTax, "2026-01-01", 0.0713m),
            Rate(PriceRateKind.ElectricityTax, "2027-01-01", 0.0800m),
            Rate(PriceRateKind.EnovaFee, "2026-01-01", 0.0100m)
        ],
        [
            Grid(TariffA, "2026-01-01", 0.30m, 0.20m),
            Grid(TariffB, "2026-06-01", 0.40m, 0.25m),
            Grid(TariffA, "2027-01-01", 0.35m, 0.22m)
        ]);

    [Fact]
    public void ValueFor_BeforeAndAfterAStartDate_IsTheValueThatWasValid()
    {
        Assert.Equal(0.0713m, Book().ValueFor(PriceRateKind.ElectricityTax, Utc("2026-12-15T10:00:00Z")));
        Assert.Equal(0.0800m, Book().ValueFor(PriceRateKind.ElectricityTax, Utc("2027-01-15T10:00:00Z")));
    }

    [Fact]
    public void ValueFor_TheHourThatStartsAtMidnightInOslo_UsesTheNewValue()
    {
        // 23:00Z on 31 December is 00:00 on 1 January in Oslo.
        Assert.Equal(0.0713m, Book().ValueFor(PriceRateKind.ElectricityTax, Utc("2026-12-31T22:00:00Z")));
        Assert.Equal(0.0800m, Book().ValueFor(PriceRateKind.ElectricityTax, Utc("2026-12-31T23:00:00Z")));
    }

    [Fact]
    public void ValueFor_AValueStartingToday_AppliesFromTheFirstHourOfToday() =>
        Assert.Equal(0.0800m, Book().ValueFor(PriceRateKind.ElectricityTax, Utc("2027-01-01T00:00:00Z")));

    [Fact]
    public void ValueFor_BeforeTheEarliestRowOfTheKind_IsNull()
    {
        Assert.Null(Book().ValueFor(PriceRateKind.ElectricityTax, Utc("2025-12-31T10:00:00Z")));
        Assert.Null(Book().ValueFor(PriceRateKind.EnovaFee, Utc("2025-06-01T10:00:00Z")));
    }

    [Fact]
    public void ValueFor_EachKindIsLookedUpSeparately()
    {
        Assert.Equal(0.25m, Book().ValueFor(PriceRateKind.Vat, Utc("2026-06-01T10:00:00Z")));
        Assert.Equal(0.0100m, Book().ValueFor(PriceRateKind.EnovaFee, Utc("2026-06-01T10:00:00Z")));
    }

    [Fact]
    public void GridRateFor_IsTheRateOfThatTariffValidForTheHour()
    {
        GridTariffRate? before = Book().GridRateFor(TariffA, Utc("2026-12-15T10:00:00Z"));
        GridTariffRate? after = Book().GridRateFor(TariffA, Utc("2027-02-01T10:00:00Z"));

        Assert.Equal(0.30m, before!.DayRatePerKwh);
        Assert.Equal(0.35m, after!.DayRatePerKwh);
        Assert.Equal(0.22m, after.NightRatePerKwh);
    }

    [Fact]
    public void GridRateFor_TheTariffsAreIndependent()
    {
        Assert.Equal(0.40m, Book().GridRateFor(TariffB, Utc("2026-12-15T10:00:00Z"))!.DayRatePerKwh);
        Assert.Equal(0.30m, Book().GridRateFor(TariffA, Utc("2026-12-15T10:00:00Z"))!.DayRatePerKwh);
    }

    [Fact]
    public void GridRateFor_BeforeTheFirstRateOfTheTariff_IsNull() =>
        Assert.Null(Book().GridRateFor(TariffB, Utc("2026-05-31T10:00:00Z")));

    [Fact]
    public void GridRateFor_AnUnknownTariff_IsNull() =>
        Assert.Null(Book().GridRateFor(Guid.NewGuid(), Utc("2026-12-15T10:00:00Z")));
}
