using Application.ElectricityCost;
using Domain.Common.Entities;

namespace Features.ElectricityCost.Services;

/// <summary>The lines of the bill, in the order the invoice shows them.</summary>
public enum BillLineCode
{
    SupplierMonthlyFee,
    SpotEnergy,
    SupplierMarkup,
    GridEnergyDay,
    GridEnergyNight,
    CapacityCharge,
    ElectricityTax,
    EnovaFee,
    NorgesPrisCredit
}

/// <summary>
/// One invoice line. Amounts include VAT unless named otherwise. When <see cref="Set"/> is false the setting the line
/// needs is missing and every amount is null (never zero); <see cref="Complete"/> is false when some hours could not
/// be priced for this line.
/// </summary>
/// <param name="PerKwh">True for a kWh line; false for a monthly line (quantity 1).</param>
/// <param name="VatRate">The VAT on the first Oslo day of the month; 0 when exempt and for the Norgespris credit.</param>
public sealed record BillLine(
    BillLineCode Code,
    bool PerKwh,
    decimal Quantity,
    decimal VatRate,
    bool Set,
    bool Complete,
    decimal? SoFar,
    decimal? Projected,
    decimal? AmountExclVat,
    decimal? Vat)
{
    public decimal? Amount => AmountExclVat + Vat;
}

/// <summary>Strøm or Nettleie: the lines and their sums over the lines that are set.</summary>
public sealed record BillPart(
    string Name, IReadOnlyList<BillLine> Lines, decimal SoFar, decimal Projected, decimal AmountExclVat, decimal Vat)
{
    public decimal Amount => AmountExclVat + Vat;
}

/// <param name="HoursWithReadings">The hours so far that have a reading.</param>
/// <param name="HoursPriced">Of those, the hours the calculation could price.</param>
public sealed record MonthlyBill(
    PricingModel PricingModel,
    BillPart Strom,
    BillPart Nettleie,
    decimal SoFar,
    decimal Projected,
    decimal AmountExclVat,
    decimal Vat,
    int HoursWithReadings,
    int HoursPriced,
    bool IsComplete)
{
    public decimal Amount => AmountExclVat + Vat;

    public BillLine Line(BillLineCode code) => Strom.Lines.Concat(Nettleie.Lines).Single(l => l.Code == code);
}

/// <summary>
/// Folds hours priced by the one calculation (<see cref="CostCalculator"/>) into the lines of the invoice, so the bill
/// can never disagree with the hourly costs. On Norgespris the supplier bills the spot price for every kWh and the grid
/// company credits (spot − Norgespris) × (1 + VAT) per kWh up to the monthly cap, without VAT on the credit; the two
/// lines add up to exactly the energy part of the hourly cost. Pure: no repositories, no rounding.
/// </summary>
public static class MonthlyBillBuilder
{
    /// <param name="soFar">The hours that have passed, one per hour with a reading.</param>
    /// <param name="projected">The projected hours of the rest of the month; empty for a month that has ended.</param>
    /// <param name="capacity">The capacity charge of the month (from the hours so far).</param>
    /// <param name="monthVatFraction">VAT on the first Oslo day of the month, 0 for a VAT-exempt location.</param>
    public static MonthlyBill Build(
        Location location,
        IReadOnlyList<HourCost> soFar,
        IReadOnlyList<HourCost> projected,
        CapacityChargeResult capacity,
        decimal monthVatFraction)
    {
        bool norgesPris = location.HasNorgesPriceAgreement;
        var sums = Enum.GetValues<BillLineCode>().ToDictionary(c => c, _ => new LineSum());

        int priced = 0;
        bool unpricedProjected = false;
        foreach ((HourCost hour, bool isProjected) in soFar.Select(h => (h, false)).Concat(projected.Select(h => (h, true))))
        {
            if (hour.Cost.Actual is null)
            {
                unpricedProjected |= isProjected;
                continue;
            }

            if (!isProjected)
            {
                priced++;
            }

            AddHour(sums, hour, isProjected, norgesPris);
        }

        decimal vatRate = location.VatExempt ? 0m : monthVatFraction;

        // The monthly amounts are a full month, never prorated; they are known, so they count as so far.
        if (location.SupplierMonthlyFee is { } fee)
        {
            sums[BillLineCode.SupplierMonthlyFee].Add(1m, fee, fee * vatRate, false);
        }

        if (capacity.Set)
        {
            sums[BillLineCode.CapacityCharge].Add(1m, capacity.AmountExclVat!.Value, capacity.Vat!.Value, false);
        }

        bool gridSet = location.GridTariffId is not null;
        BillLine Line(BillLineCode code, bool perKwh, bool set, decimal rate) => sums[code].ToLine(code, perKwh, set, rate);

        var stromLines = new List<BillLine>
        {
            Line(BillLineCode.SupplierMonthlyFee, false, location.SupplierMonthlyFee is not null, vatRate),
            Line(BillLineCode.SpotEnergy, true, true, vatRate),
            Line(BillLineCode.SupplierMarkup, true, true, vatRate)
        };
        var nettleieLines = new List<BillLine>
        {
            Line(BillLineCode.GridEnergyDay, true, gridSet, vatRate),
            Line(BillLineCode.GridEnergyNight, true, gridSet, vatRate),
            Line(BillLineCode.CapacityCharge, false, capacity.Set, vatRate),
            Line(BillLineCode.ElectricityTax, true, true, vatRate),
            Line(BillLineCode.EnovaFee, true, true, vatRate)
        };
        if (norgesPris)
        {
            nettleieLines.Add(Line(BillLineCode.NorgesPrisCredit, true, true, 0m));
        }

        BillPart strom = Part("Strom", stromLines);
        BillPart nettleie = Part("Nettleie", nettleieLines);
        bool complete = stromLines.Concat(nettleieLines).All(l => l.Set && l.Complete)
                        && priced == soFar.Count
                        && !unpricedProjected;

        return new MonthlyBill(
            norgesPris ? PricingModel.NorgesPris : PricingModel.Spot,
            strom,
            nettleie,
            strom.SoFar + nettleie.SoFar,
            strom.Projected + nettleie.Projected,
            strom.AmountExclVat + nettleie.AmountExclVat,
            strom.Vat + nettleie.Vat,
            soFar.Count,
            priced,
            complete);
    }

    private static void AddHour(Dictionary<BillLineCode, LineSum> sums, HourCost hour, bool projected, bool norgesPris)
    {
        RateCost actual = hour.Cost.Actual!;
        decimal kwh = hour.ConsumptionKwh;
        BillLineCode gridLine = GridPeriods.For(hour.HourStart) == GridPeriod.Day
            ? BillLineCode.GridEnergyDay
            : BillLineCode.GridEnergyNight;

        // The kWh within the Norgespris cap and above it; a spot location has all its kWh on spot terms.
        decimal within = norgesPris ? actual.Cap?.KwhWithinCap ?? kwh : 0m;
        decimal above = kwh - within;
        PriceBreakdown withinTerms = actual.Breakdown;
        PriceBreakdown aboveTerms = norgesPris ? actual.Cap?.AboveCapBreakdown ?? actual.Breakdown : actual.Breakdown;

        // Markup, grid and taxes are the same per kWh on both terms of an hour; take them from the terms each kWh had.
        foreach ((decimal part, PriceBreakdown terms) in new[] { (within, withinTerms), (above, aboveTerms) })
        {
            if (part == 0m)
            {
                continue;
            }

            decimal v = terms.VatRate;
            sums[BillLineCode.SupplierMarkup].Add(part, part * terms.Markup, part * terms.Markup * v, projected);
            sums[BillLineCode.ElectricityTax].Add(
                part, part * terms.ElectricityTax, part * terms.ElectricityTax * v, projected);
            sums[BillLineCode.EnovaFee].Add(part, part * terms.EnovaFee, part * terms.EnovaFee * v, projected);
            if (terms.Grid is { } grid)
            {
                sums[gridLine].Add(part, part * grid, part * grid * v, projected);
            }
            else
            {
                sums[gridLine].Incomplete = true;
            }
        }

        if (!norgesPris)
        {
            decimal energy = actual.Breakdown.Energy;
            decimal v = actual.Breakdown.VatRate;
            sums[BillLineCode.SpotEnergy].Add(kwh, kwh * energy, kwh * energy * v, projected);
            return;
        }

        // The spot terms of the whole hour: what the supplier bills for every kWh.
        PriceBreakdown? spot = hour.Cost.Comparison?.Breakdown;
        LineSum credit = sums[BillLineCode.NorgesPrisCredit];
        if (spot is null)
        {
            // No spot price, so the split between the two lines is unknown; within the cap the net is still known
            // (the Norgespris rate), so it goes on the energy line and the total stays right.
            decimal net = within * withinTerms.Energy;
            sums[BillLineCode.SpotEnergy].Add(kwh, net, net * withinTerms.VatRate, projected);
            sums[BillLineCode.SpotEnergy].Incomplete = true;
            credit.Add(within, 0m, 0m, projected);
            credit.Incomplete = true;
            return;
        }

        sums[BillLineCode.SpotEnergy].Add(kwh, kwh * spot.Energy, kwh * spot.Energy * spot.VatRate, projected);
        if (within > 0m)
        {
            // The credit carries no VAT of its own: it is the difference including VAT.
            decimal amount = -within * (spot.Energy - withinTerms.Energy) * (1m + withinTerms.VatRate);
            credit.Add(within, amount, 0m, projected);
        }
    }

    private static BillPart Part(string name, List<BillLine> lines)
    {
        var set = lines.Where(l => l.Set).ToList();
        return new BillPart(
            name,
            lines,
            set.Sum(l => l.SoFar!.Value),
            set.Sum(l => l.Projected!.Value),
            set.Sum(l => l.AmountExclVat!.Value),
            set.Sum(l => l.Vat!.Value));
    }

    private sealed class LineSum
    {
        private decimal _quantity;
        private decimal _soFarExclVat;
        private decimal _soFarVat;
        private decimal _projectedExclVat;
        private decimal _projectedVat;

        public bool Incomplete { get; set; }

        public void Add(decimal quantity, decimal exclVat, decimal vat, bool projected)
        {
            _quantity += quantity;
            if (projected)
            {
                _projectedExclVat += exclVat;
                _projectedVat += vat;
            }
            else
            {
                _soFarExclVat += exclVat;
                _soFarVat += vat;
            }
        }

        public BillLine ToLine(BillLineCode code, bool perKwh, bool set, decimal vatRate) => set
            ? new BillLine(
                code,
                perKwh,
                perKwh ? _quantity : 1m,
                vatRate,
                true,
                !Incomplete,
                _soFarExclVat + _soFarVat,
                _projectedExclVat + _projectedVat,
                _soFarExclVat + _projectedExclVat,
                _soFarVat + _projectedVat)
            : new BillLine(code, perKwh, perKwh ? _quantity : 1m, vatRate, false, true, null, null, null, null);
    }
}
