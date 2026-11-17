namespace Domain.Common;

/// <summary>
/// The national dated values: the ones that make up the price besides the energy and the grid tariff, and the
/// Norgespris monthly caps (kWh per month).
/// </summary>
public enum PriceRateKind
{
    ElectricityTax,
    EnovaFee,
    Vat,
    NorgesPrisCapHome,
    NorgesPrisCapHolidayHome
}
