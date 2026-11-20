using System.Globalization;
using Application.Common.Interfaces.Repositories;
using Application.ElectricityCost;
using Domain.Common;
using Domain.Common.Entities;
using Features.ElectricityCost.Services;
using Features.Tests.NorgesPris;
using Features.Tests.Pricing;
using NSubstitute;

namespace Features.Tests.ElectricityCost;

/// <summary>
/// The bill lines, against values worked out by hand. The hours are priced by the real calculator, so the bill is
/// checked against the same breakdowns the hourly costs use. Unless a test says otherwise: spot 100 EUR/MWh at 10 NOK
/// per EUR (1.00 NOK per kWh), Norgespris 0.40, markup 0.02, grid 0.30 day and 0.20 night, electricity tax 0.0713,
/// Enova fee 0.0100, VAT 25 percent, monthly fee 27.20, holiday-home cap 1,000 kWh.
/// </summary>
public class MonthlyBillBuilderTests
{
    private static readonly Guid Tariff = Guid.NewGuid();

    // Wednesday 2026-12-02 14:00 Oslo (a day hour) and Sunday 2026-12-06 14:00 Oslo (a night hour).
    private static readonly DateTime WeekdayAfternoon = Utc("2026-12-02T13:00:00Z");
    private static readonly DateTime SundayAfternoon = Utc("2026-12-06T13:00:00Z");

    private readonly IElectricityPriceRepository<ElectricityPrice> _prices =
        Substitute.For<IElectricityPriceRepository<ElectricityPrice>>();

    private readonly IExchangeRateRepository<ExchangeRate> _fx =
        Substitute.For<IExchangeRateRepository<ExchangeRate>>();

    private readonly HashSet<DateTime> _hoursWithoutSpotPrice = [];
    private decimal _spotEurPerMwh = 100m;

    public MonthlyBillBuilderTests()
    {
        _prices.GetByRegionAndHourAsync("NO1", Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => _hoursWithoutSpotPrice.Contains(call.ArgAt<DateTime>(1))
                ? null
                : new ElectricityPrice(Guid.NewGuid(), "NO1", call.ArgAt<DateTime>(1), _spotEurPerMwh));
        _fx.GetByDateAsync("EURNOK", Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => new ExchangeRate(Guid.NewGuid(), "EURNOK", call.ArgAt<DateOnly>(1), 10m));
    }

    private static DateTime Utc(string iso) =>
        DateTime.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    private static Location LocationWith(
        bool norgesPris,
        Guid? tariff = null,
        decimal? fee = 27.20m,
        bool taxExempt = false,
        bool vatExempt = false,
        decimal markup = 0.02m)
    {
        var location = new Location(Guid.NewGuid(), "Cabin", "Addr", "SN1", "NO1", true, norgesPris);
        location.SetPricing(tariff, markup, taxExempt, vatExempt, true);
        location.SetSupplierMonthlyFee(fee);
        return location;
    }

    private static IPriceRateRepository Rates(params GridTariffRate[] grid) => PriceRateStubs.Repository(
        PriceRateStubs.National2026().Append(
            PriceRateStubs.Rate(PriceRateKind.NorgesPrisCapHolidayHome, "2025-10-01", 1000m)),
        grid.Length > 0 ? grid : [PriceRateStubs.Grid(Tariff, "2026-01-01", 0.30m, 0.20m)]);

    private CostCalculator Calculator(IPriceRateRepository? rates = null) =>
        new(_prices, _fx, NorgesPrisRates.Seeded(), rates ?? Rates());

    /// <summary>Prices the hours in order with a running month kWh, as <see cref="HourlyCostProvider"/> does.</summary>
    private static async Task<List<HourCost>> Price(
        CostCalculator calculator, Location location, IEnumerable<(DateTime Hour, decimal Kwh)> hours,
        decimal monthKwhBefore = 0m)
    {
        var result = new List<HourCost>();
        decimal month = monthKwhBefore;
        foreach ((DateTime hour, decimal kwh) in hours)
        {
            result.Add(new HourCost(hour, kwh, await calculator.CalculateAsync(location, hour, kwh, month, default)));
            month += kwh;
        }

        return result;
    }

    private static GridCapacityStep Step(decimal lower, decimal? upper, decimal amount, string from = "2026-01-01") =>
        new(Guid.NewGuid(), Tariff, DateOnly.Parse(from, CultureInfo.InvariantCulture), lower, upper, amount, Guid.Empty);

    /// <summary>One step from 0 kW at 100 NOK excluding VAT: 125 including VAT.</summary>
    private static CapacityChargeResult Capacity(IEnumerable<HourCost> hours, decimal vat = 0.25m) =>
        CapacityCharge.Calculate(hours.Select(h => (h.HourStart, h.ConsumptionKwh)), [Step(0m, null, 100m)], vat);

    private static readonly CapacityChargeResult NoCapacity = CapacityCharge.Calculate([], [], 0.25m);

    private static (DateTime, decimal)[] TwoHours => [(WeekdayAfternoon, 2m), (SundayAfternoon, 3m)];

    // ---- invariants -------------------------------------------------------------------------------------------

    /// <summary>The sums of data-model.md hold for every line, part and the total, with no rounding gap.</summary>
    private static void AssertAddsUp(MonthlyBill bill)
    {
        foreach (BillPart part in new[] { bill.Strom, bill.Nettleie })
        {
            foreach (BillLine line in part.Lines.Where(l => l.Set))
            {
                Assert.Equal(line.Amount, line.SoFar + line.Projected);
                Assert.Equal(line.Amount, line.AmountExclVat + line.Vat);
            }

            Assert.Equal(part.Lines.Where(l => l.Set).Sum(l => l.Amount!.Value), part.Amount);
            Assert.Equal(part.Amount, part.SoFar + part.Projected);
        }

        Assert.Equal(bill.Strom.Amount + bill.Nettleie.Amount, bill.Amount);
        Assert.Equal(bill.Amount, bill.SoFar + bill.Projected);
    }

    /// <summary>
    /// SC-003: spot energy + credit + markup equals the energy and markup of the hourly costs, which is the hour's cost
    /// minus its grid and taxes (the same per kWh on both terms of an hour).
    /// </summary>
    private static void AssertNetEnergyEqualsTheHourlyCosts(MonthlyBill bill, IEnumerable<HourCost> hours)
    {
        decimal hourly = hours.Where(h => h.Cost.Actual is not null).Sum(h =>
        {
            PriceBreakdown b = h.Cost.Actual!.Breakdown;
            return h.Cost.Actual.Cost - h.ConsumptionKwh * ((b.Grid ?? 0m) + b.Taxes) * (1m + b.VatRate);
        });
        decimal credit = bill.PricingModel == PricingModel.NorgesPris
            ? bill.Line(BillLineCode.NorgesPrisCredit).Amount!.Value
            : 0m;

        Assert.Equal(
            hourly,
            bill.Line(BillLineCode.SpotEnergy).Amount!.Value + credit
            + bill.Line(BillLineCode.SupplierMarkup).Amount!.Value);
    }

    // ---- Norgespris and spot -----------------------------------------------------------------------------------

    [Fact]
    public async Task ANorgesPrisMonthBelowTheCap_HasEveryLineByHand()
    {
        Location cabin = LocationWith(true, Tariff);
        List<HourCost> hours = await Price(Calculator(), cabin, TwoHours);

        MonthlyBill bill = MonthlyBillBuilder.Build(cabin, hours, [], Capacity(hours), 0.25m);

        Assert.Equal(34.00m, bill.Line(BillLineCode.SupplierMonthlyFee).Amount);
        Assert.Equal(6.8m, bill.Line(BillLineCode.SupplierMonthlyFee).Vat);
        Assert.Equal(6.25m, bill.Line(BillLineCode.SpotEnergy).Amount);         // 5 x 1.00 x 1.25
        Assert.Equal(0.125m, bill.Line(BillLineCode.SupplierMarkup).Amount);    // 5 x 0.02 x 1.25
        Assert.Equal(0.75m, bill.Line(BillLineCode.GridEnergyDay).Amount);      // 2 x 0.30 x 1.25
        Assert.Equal(2m, bill.Line(BillLineCode.GridEnergyDay).Quantity);
        Assert.Equal(0.75m, bill.Line(BillLineCode.GridEnergyNight).Amount);    // 3 x 0.20 x 1.25
        Assert.Equal(3m, bill.Line(BillLineCode.GridEnergyNight).Quantity);
        Assert.Equal(125m, bill.Line(BillLineCode.CapacityCharge).Amount);
        Assert.Equal(0.445625m, bill.Line(BillLineCode.ElectricityTax).Amount); // 5 x 0.0713 x 1.25
        Assert.Equal(0.0625m, bill.Line(BillLineCode.EnovaFee).Amount);         // 5 x 0.0100 x 1.25

        BillLine credit = bill.Line(BillLineCode.NorgesPrisCredit);
        Assert.Equal(-3.75m, credit.Amount); // -5 x (1.00 - 0.40) x 1.25
        Assert.Equal(0m, credit.Vat);
        Assert.Equal(0m, credit.VatRate);
        Assert.Equal(5m, credit.Quantity);

        Assert.Equal(40.375m, bill.Strom.Amount);
        Assert.Equal(123.258125m, bill.Nettleie.Amount);
        Assert.Equal(163.633125m, bill.Amount);
        Assert.True(bill.IsComplete);
        Assert.Equal(2, bill.HoursPriced);
        AssertAddsUp(bill);
        AssertNetEnergyEqualsTheHourlyCosts(bill, hours);
    }

    [Fact]
    public async Task ASpotLocation_HasNoCredit_AndTheSpotEnergyIsTheEnergyCost()
    {
        Location home = LocationWith(false, Tariff);
        List<HourCost> hours = await Price(Calculator(), home, TwoHours);

        MonthlyBill bill = MonthlyBillBuilder.Build(home, hours, [], Capacity(hours), 0.25m);

        Assert.DoesNotContain(bill.Nettleie.Lines, l => l.Code == BillLineCode.NorgesPrisCredit);
        Assert.Equal(PricingModel.Spot, bill.PricingModel);
        Assert.Equal(6.25m, bill.Line(BillLineCode.SpotEnergy).Amount);
        Assert.Equal(0.125m, bill.Line(BillLineCode.SupplierMarkup).Amount);
        Assert.Equal(hours.Sum(h => h.Cost.Actual!.Cost), bill.Amount - 34m - 125m);
        AssertAddsUp(bill);
        AssertNetEnergyEqualsTheHourlyCosts(bill, hours);
    }

    [Fact]
    public async Task AnHourWithSpotBelowNorgesPris_AddsACharge()
    {
        // Spot 30 EUR/MWh = 0.30 NOK/kWh: -1 x (0.30 - 0.40) x 1.25 = +0.125.
        _spotEurPerMwh = 30m;
        Location cabin = LocationWith(true, Tariff);
        List<HourCost> hours = await Price(Calculator(), cabin, [(WeekdayAfternoon, 1m)]);

        MonthlyBill bill = MonthlyBillBuilder.Build(cabin, hours, [], Capacity(hours), 0.25m);

        Assert.Equal(0.125m, bill.Line(BillLineCode.NorgesPrisCredit).Amount);
        AssertNetEnergyEqualsTheHourlyCosts(bill, hours);
    }

    [Fact]
    public async Task AboveTheCap_TheKwhGetNoCredit()
    {
        // 999 kWh before: the 3 kWh hour has 1 kWh within the cap and 2 above; the next hour is all above.
        Location cabin = LocationWith(true, Tariff);
        List<HourCost> hours = await Price(
            Calculator(), cabin, [(WeekdayAfternoon, 3m), (SundayAfternoon, 2m)], monthKwhBefore: 999m);

        MonthlyBill bill = MonthlyBillBuilder.Build(cabin, hours, [], Capacity(hours), 0.25m);

        BillLine credit = bill.Line(BillLineCode.NorgesPrisCredit);
        Assert.Equal(-0.75m, credit.Amount); // -1 x 0.60 x 1.25
        Assert.Equal(1m, credit.Quantity);
        Assert.Equal(6.25m, bill.Line(BillLineCode.SpotEnergy).Amount); // every kWh at spot: 5 x 1.25
        Assert.Equal(0.125m, bill.Line(BillLineCode.SupplierMarkup).Amount);
        AssertAddsUp(bill);
        AssertNetEnergyEqualsTheHourlyCosts(bill, hours);
    }

    // ---- exemptions --------------------------------------------------------------------------------------------

    [Fact]
    public async Task AVatExemptLocation_HasNoVatOnAnyLine()
    {
        Location cabin = LocationWith(true, Tariff, vatExempt: true);
        List<HourCost> hours = await Price(Calculator(), cabin, TwoHours);

        MonthlyBill bill = MonthlyBillBuilder.Build(cabin, hours, [], Capacity(hours, vat: 0m), 0.25m);

        Assert.All(bill.Strom.Lines.Concat(bill.Nettleie.Lines), l => Assert.Equal(0m, l.Vat));
        Assert.All(bill.Strom.Lines.Concat(bill.Nettleie.Lines), l => Assert.Equal(0m, l.VatRate));
        Assert.Equal(27.20m, bill.Line(BillLineCode.SupplierMonthlyFee).Amount);
        Assert.Equal(-3.00m, bill.Line(BillLineCode.NorgesPrisCredit).Amount); // -5 x 0.60
        Assert.Equal(0m, bill.Vat);
        AssertAddsUp(bill);
        AssertNetEnergyEqualsTheHourlyCosts(bill, hours);
    }

    [Fact]
    public async Task ATaxExemptLocation_HasAZeroTaxLine_AndStillTheEnovaFee()
    {
        Location cabin = LocationWith(true, Tariff, taxExempt: true);
        List<HourCost> hours = await Price(Calculator(), cabin, TwoHours);

        MonthlyBill bill = MonthlyBillBuilder.Build(cabin, hours, [], Capacity(hours), 0.25m);

        Assert.Equal(0m, bill.Line(BillLineCode.ElectricityTax).Amount);
        Assert.True(bill.Line(BillLineCode.ElectricityTax).Set);
        Assert.Equal(0.0625m, bill.Line(BillLineCode.EnovaFee).Amount);
        AssertAddsUp(bill);
    }

    // ---- gaps --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ANorgesPrisHourWithoutASpotPrice_KeepsTheTotal_ButMarksTheSplitIncomplete()
    {
        _hoursWithoutSpotPrice.Add(WeekdayAfternoon);
        Location cabin = LocationWith(true, Tariff);
        List<HourCost> hours = await Price(Calculator(), cabin, [(WeekdayAfternoon, 1m)]);

        MonthlyBill bill = MonthlyBillBuilder.Build(cabin, hours, [], Capacity(hours), 0.25m);

        Assert.Equal(0.5m, bill.Line(BillLineCode.SpotEnergy).Amount); // the net: 1 x 0.40 x 1.25
        Assert.Equal(0m, bill.Line(BillLineCode.NorgesPrisCredit).Amount);
        Assert.False(bill.Line(BillLineCode.SpotEnergy).Complete);
        Assert.False(bill.Line(BillLineCode.NorgesPrisCredit).Complete);
        Assert.Equal(1, bill.HoursPriced);
        Assert.False(bill.IsComplete);
        AssertAddsUp(bill);
        AssertNetEnergyEqualsTheHourlyCosts(bill, hours);
    }

    [Fact]
    public async Task AnHourThatCannotBePriced_IsLeftOut_AndTheBillIsIncomplete()
    {
        _hoursWithoutSpotPrice.Add(WeekdayAfternoon);
        Location home = LocationWith(false, Tariff);
        List<HourCost> hours = await Price(Calculator(), home, TwoHours);

        MonthlyBill bill = MonthlyBillBuilder.Build(home, hours, [], Capacity(hours), 0.25m);

        Assert.Equal(2, bill.HoursWithReadings);
        Assert.Equal(1, bill.HoursPriced);
        Assert.Equal(3m, bill.Line(BillLineCode.SpotEnergy).Quantity);
        Assert.False(bill.IsComplete);
        AssertAddsUp(bill);
    }

    [Fact]
    public async Task AMissingFeeTariffAndCapacity_AreNotSet_NeverZero()
    {
        Location cabin = LocationWith(true, tariff: null, fee: null);
        List<HourCost> hours = await Price(Calculator(), cabin, TwoHours);

        MonthlyBill bill = MonthlyBillBuilder.Build(cabin, hours, [], NoCapacity, 0.25m);

        foreach (BillLineCode code in new[]
                 {
                     BillLineCode.SupplierMonthlyFee, BillLineCode.GridEnergyDay, BillLineCode.GridEnergyNight,
                     BillLineCode.CapacityCharge
                 })
        {
            BillLine line = bill.Line(code);
            Assert.False(line.Set);
            Assert.Null(line.Amount);
            Assert.Null(line.SoFar);
        }

        // 6.25 - 3.75 + 0.125 + 0.445625 + 0.0625: only the lines that are set.
        Assert.Equal(3.133125m, bill.Amount);
        Assert.False(bill.IsComplete);
        AssertAddsUp(bill);
    }

    // ---- the July 2026 invoice (SC-001) ------------------------------------------------------------------------

    [Fact]
    public async Task TheJuly2026Invoice_IsReproducedWithinTheTolerances()
    {
        // Fjordkraft and Elvia, July 2026, a holiday home on Norgespris: 95.58 kWh on weekday day hours and 144.73 kWh
        // at night and weekends, the invoice spot price 155.52 ore incl. VAT (1.24416 excl.) on every hour, three daily
        // peaks of 6 kWh (step 5-10 kW, 336 excl. VAT), markup 3.99 ore incl. VAT, fee 34.00 kr incl. VAT. The markup
        // is 0.03192 excl. VAT on the invoice, but a markup is stored with 4 decimals, so the location has 0.0319.
        _spotEurPerMwh = 124.416m;
        IPriceRateRepository rates = Rates(PriceRateStubs.Grid(Tariff, "2026-07-01", 0.2899m, 0.1699m));
        Location cabin = LocationWith(true, Tariff, fee: 27.20m, markup: 0.0319m);

        DateTime monthStart = Utc("2026-06-30T22:00:00Z"); // 1 July 00:00 Oslo (summer time)
        var all = Enumerable.Range(0, 744).Select(h => monthStart.AddHours(h)).ToList();
        DateTime[] peaks = [Utc("2026-07-04T10:00:00Z"), Utc("2026-07-11T10:00:00Z"), Utc("2026-07-18T10:00:00Z")];
        var day = all.Where(h => GridPeriods.For(h) == GridPeriod.Day).ToList();
        var night = all.Where(h => GridPeriods.For(h) == GridPeriod.Night && !peaks.Contains(h)).ToList();

        var kwh = new Dictionary<DateTime, decimal>();
        Spread(kwh, day, 95.58m);
        Spread(kwh, night, 144.73m - 18m);
        foreach (DateTime peak in peaks)
        {
            kwh[peak] = 6m;
        }

        List<HourCost> hours = await Price(Calculator(rates), cabin, all.Select(h => (h, kwh[h])));
        GridCapacityStep[] table =
        [
            Step(0m, 2m, 120m, "2026-07-01"), Step(2m, 5m, 200m, "2026-07-01"), Step(5m, 10m, 336m, "2026-07-01"),
            Step(10m, 15m, 468m, "2026-07-01"), Step(15m, null, 604m, "2026-07-01")
        ];
        CapacityChargeResult capacity = CapacityCharge.Calculate(
            hours.Select(h => (h.HourStart, h.ConsumptionKwh)), table, 0.25m);

        MonthlyBill bill = MonthlyBillBuilder.Build(cabin, hours, [], capacity, 0.25m);

        Assert.InRange(bill.Amount, 673.55m - 0.10m, 673.55m + 0.10m);
        AssertNear(34.00m, BillLineCode.SupplierMonthlyFee, 0.02m);
        AssertNear(9.59m, BillLineCode.SupplierMarkup, 0.02m);
        AssertNear(34.64m, BillLineCode.GridEnergyDay, 0.02m);
        AssertNear(30.74m, BillLineCode.GridEnergyNight, 0.02m);
        AssertNear(420.00m, BillLineCode.CapacityCharge, 0.02m);
        AssertNear(8.51m + 12.90m, BillLineCode.ElectricityTax, 0.02m);
        AssertNear(1.20m + 1.81m, BillLineCode.EnovaFee, 0.02m);
        AssertNear(373.74m, BillLineCode.SpotEnergy, 1.00m);
        AssertNear(-253.58m, BillLineCode.NorgesPrisCredit, 1.00m);
        Assert.InRange(bill.Strom.Amount, 417.33m - 1m, 417.33m + 1m);
        Assert.InRange(bill.Nettleie.Amount, 256.22m - 1m, 256.22m + 1m);
        decimal net = bill.Line(BillLineCode.SpotEnergy).Amount!.Value
                      + bill.Line(BillLineCode.NorgesPrisCredit).Amount!.Value;
        Assert.InRange(net, 120.16m - 0.02m, 120.16m + 0.02m);
        Assert.Equal(95.58m, bill.Line(BillLineCode.GridEnergyDay).Quantity);
        Assert.Equal(144.73m, bill.Line(BillLineCode.GridEnergyNight).Quantity);
        Assert.True(bill.IsComplete);
        AssertAddsUp(bill);
        AssertNetEnergyEqualsTheHourlyCosts(bill, hours);

        void AssertNear(decimal invoice, BillLineCode code, decimal tolerance) =>
            Assert.InRange(bill.Line(code).Amount!.Value, invoice - tolerance, invoice + tolerance);
    }

    /// <summary>Spreads a total evenly over the hours, the remainder on the last, so the sum is exact.</summary>
    private static void Spread(Dictionary<DateTime, decimal> kwh, List<DateTime> hours, decimal total)
    {
        decimal each = Math.Round(total / hours.Count, 6);
        foreach (DateTime hour in hours)
        {
            kwh[hour] = each;
        }

        kwh[hours[^1]] = total - each * (hours.Count - 1);
    }
}
