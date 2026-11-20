using Application.ElectricityCost;
using Application.WaterHeaters;
using Features.ElectricityCost.Queries;
using Features.ElectricityCost.Services;

namespace Features.ElectricityCost.Mappers;

public static class BillMapper
{
    public const string Final = "Final";
    public const string Estimate = "Estimate";

    public static MonthlyBillResponse ToResponse(
        DateOnly month,
        bool ended,
        MonthlyBill bill,
        BillHoursResponse hours,
        BillProjectionResponse? projection,
        CapacityChargeResult capacity,
        WaterHeaterTotal? waterHeater) =>
        new(
            month.ToString("yyyy-MM"),
            ended ? Final : Estimate,
            bill.PricingModel.ToString(),
            bill.IsComplete,
            hours,
            projection,
            new BillCapacityResponse(
                !ended,
                capacity.Set ? capacity.AverageKw : null,
                capacity.Step is { } step ? new CapacityStepRangeResponse(step.LowerKw, step.UpperKw) : null),
            ToPart(bill.Strom),
            ToPart(bill.Nettleie),
            new BillTotalResponse(bill.SoFar, bill.Projected, bill.Amount, bill.AmountExclVat, bill.Vat),
            ToWaterHeater(waterHeater));

    private static WaterHeaterBillResponse? ToWaterHeater(WaterHeaterTotal? total) =>
        total is null
            ? null
            : new WaterHeaterBillResponse(
                total.Kwh, total.Cost, total.SampledMinutes, total.Capped, total.FirstHourUtc, total.LastHourUtc,
                Estimate: false);

    private static BillPartResponse ToPart(BillPart part) =>
        new(
            part.Name,
            part.Lines.Select(ToLine).ToList(),
            part.SoFar,
            part.Projected,
            part.Amount,
            part.AmountExclVat,
            part.Vat);

    private static BillLineResponse ToLine(BillLine line) =>
        new(
            line.Code.ToString(),
            line.PerKwh ? "kWh" : "Month",
            line.Quantity,
            line.Set && line.Quantity != 0m ? line.Amount / line.Quantity : null,
            line.VatRate,
            line.Set,
            line.Complete,
            line.SoFar,
            line.Projected,
            line.Amount,
            line.AmountExclVat,
            line.Vat);
}
