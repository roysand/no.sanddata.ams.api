namespace Application.ElectricityCost;

/// <summary>
/// The parts of the price of one kWh in one hour, in NOK per kWh. <see cref="Grid"/> is null when no grid tariff is set
/// for the location (or the tariff has no rate for the hour); <see cref="GridSet"/> says so. The parts add up exactly:
/// Energy + Markup + (Grid ?? 0) + Taxes + Vat = Total.
/// </summary>
public record PriceBreakdown(
    decimal Energy, decimal Markup, decimal? Grid, decimal Taxes, decimal Vat, decimal Total, bool GridSet);

/// <summary>The one formula for the price of an hour. Pure: no repositories, no rounding.</summary>
public static class PriceCalculation
{
    /// <param name="energy">Spot price or Norgespris rate, NOK per kWh excluding VAT.</param>
    /// <param name="markup">Supplier markup, NOK per kWh excluding VAT, on spot and Norgespris terms alike.</param>
    /// <param name="grid">Grid energy charge excluding taxes and VAT, or null when not set.</param>
    /// <param name="electricityTax">Electricity tax, NOK per kWh.</param>
    /// <param name="enovaFee">Enova fee, NOK per kWh.</param>
    /// <param name="vatFraction">VAT as a fraction (0.25 for 25 percent).</param>
    public static PriceBreakdown Calculate(
        decimal energy, decimal markup, decimal? grid, decimal electricityTax, decimal enovaFee, decimal vatFraction)
    {
        decimal taxes = electricityTax + enovaFee;
        decimal net = energy + markup + (grid ?? 0m) + taxes;
        decimal vat = vatFraction * net;

        return new PriceBreakdown(energy, markup, grid, taxes, vat, net + vat, grid is not null);
    }
}
