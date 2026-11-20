using Application.CQRS;
using Domain.Common;

namespace Features.ElectricityCost.Queries;

/// <param name="Month">A day in the Oslo calendar month, or null for the current month.</param>
public record GetMonthlyBillQuery(Guid UserId, Guid LocationId, DateOnly? Month) : IQuery<Result<MonthlyBillResponse>>;

/// <param name="InMonth">The hours of the Oslo month (743, 744 or 745 in a clock-change month).</param>
/// <param name="Passed">The hours of the month before the start of the current hour.</param>
/// <param name="WithReadings">Of those, the hours that have a reading; a gap is never filled in.</param>
/// <param name="Priced">Of those, the hours the calculation could price (a missing spot price or rate leaves one out).</param>
public record BillHoursResponse(int InMonth, int Passed, int WithReadings, int Priced);

/// <summary>How the rest of the current month is projected; null on a month that has ended.</summary>
/// <param name="FromUtc">The start of the current hour: it and every later hour of the month are projected.</param>
/// <param name="BasedOnHours">The hours with readings in the 7 days the profile is taken from (0 to 168).</param>
/// <param name="AssumedSpotPricePerKwh">
/// The spot price, NOK per kWh excluding VAT, used for hours without a published price; null when none was found.
/// </param>
public record BillProjectionResponse(
    DateTime FromUtc, int Hours, decimal Kwh, int BasedOnHours, decimal BasedOnDays, decimal? AssumedSpotPricePerKwh);

/// <param name="Provisional">True until the month has ended: the step can still go up.</param>
/// <param name="AverageKw">The average of the daily peaks; null when the charge is not set.</param>
public record BillCapacityResponse(bool Provisional, decimal? AverageKw, CapacityStepRangeResponse? Step);

/// <summary>One invoice line. Amounts in NOK, including VAT unless named otherwise, never rounded.</summary>
/// <param name="Code">
/// SupplierMonthlyFee, SpotEnergy, SupplierMarkup, GridEnergyDay, GridEnergyNight, CapacityCharge, ElectricityTax,
/// EnovaFee or NorgesPrisCredit.
/// </param>
/// <param name="Unit">kWh or Month.</param>
/// <param name="UnitPrice">Amount / quantity; null when the quantity is 0 or the line is not set.</param>
/// <param name="VatRate">The VAT on the first Oslo day of the month; 0 when exempt and for the Norgespris credit.</param>
/// <param name="Set">False when the setting the line needs is missing; its amounts are then null, never zero.</param>
/// <param name="Complete">False when some hours could not be priced for this line.</param>
/// <param name="SoFar">The hours that have passed; a monthly line has its full amount here.</param>
/// <param name="Projected">The rest of the month; 0 on a month that has ended.</param>
public record BillLineResponse(
    string Code,
    string Unit,
    decimal Quantity,
    decimal? UnitPrice,
    decimal VatRate,
    bool Set,
    bool Complete,
    decimal? SoFar,
    decimal? Projected,
    decimal? Amount,
    decimal? AmountExclVat,
    decimal? Vat);

/// <param name="Name">Strom (the supplier) or Nettleie (the grid company).</param>
public record BillPartResponse(
    string Name,
    IReadOnlyList<BillLineResponse> Lines,
    decimal SoFar,
    decimal Projected,
    decimal Amount,
    decimal AmountExclVat,
    decimal Vat);

public record BillTotalResponse(decimal SoFar, decimal Projected, decimal Amount, decimal AmountExclVat, decimal Vat);

/// <param name="Month">yyyy-MM.</param>
/// <param name="Status">Final once the month has ended; Estimate before.</param>
/// <param name="PricingModel">Spot or NorgesPris.</param>
/// <param name="IsComplete">
/// False when a line is not set or not complete, or some hours with readings could not be priced.
/// </param>
public record MonthlyBillResponse(
    string Month,
    string Status,
    string PricingModel,
    bool IsComplete,
    BillHoursResponse Hours,
    BillProjectionResponse? Projection,
    BillCapacityResponse Capacity,
    BillPartResponse Strom,
    BillPartResponse Nettleie,
    BillTotalResponse Total,
    WaterHeaterBillResponse? WaterHeater,
    decimal TotalKwh,
    EvChargerBillResponse? EvCharger);

/// <summary>
/// The EV charger's kWh in the month's hours so far (spec 016), never projected and never priced: no line, subtotal,
/// total or capacity step includes it. TotalKwh (the meter's kWh so far) adds it only when <paramref name="Included"/>.
/// </summary>
public record EvChargerBillResponse(
    decimal Kwh,
    bool Estimated,
    bool Included,
    int HoursWithoutMeterReading,
    DateTime FirstHour,
    DateTime LastHour);

/// <summary>
/// The water heater's part of the month's hours so far (spec 012). It is part of the energy cost, not a bill line, and
/// never projected, so <see cref="Estimate"/> is always false. Null when the month has no heater samples.
/// </summary>
public record WaterHeaterBillResponse(
    decimal Kwh,
    decimal? Cost,
    int SampledMinutes,
    bool Capped,
    DateTime FirstHourUtc,
    DateTime LastHourUtc,
    bool Estimate);
