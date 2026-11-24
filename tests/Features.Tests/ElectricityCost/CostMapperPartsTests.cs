using Application.ElectricityCost;
using Domain.Common;
using Features.ElectricityCost.Mappers;
using Features.ElectricityCost.Queries;
using Features.ElectricityCost.Services;
using Features.Tests.Http;
using NSubstitute;

namespace Features.Tests.ElectricityCost;

[Collection(FastEndpointsHostCollection.Name)]
public class CostMapperPartsTests
{
    private static readonly DateTime Hour = new(2026, 12, 2, 13, 0, 0, DateTimeKind.Utc);

    private static RateCost Rate(decimal energy, decimal markup, decimal? grid, decimal kwh)
    {
        PriceBreakdown parts = PriceCalculation.Calculate(energy, markup, grid, 0.0713m, 0.0100m, 0.25m);
        return new RateCost(parts.Total, kwh * parts.Total, parts);
    }

    private static HourCost HourOf(decimal kwh, RateCost? actual, RateCost? comparison) =>
        new(Hour, kwh, new CostResult(PricingModel.Spot, actual, comparison));

    [Fact]
    public void TheHourlyResponse_CarriesThePartsOfTheBreakdown()
    {
        RateCost actual = Rate(1.00m, 0.02m, 0.30m, 2m);

        HourRateCostResponse response = CostMapper.ToHourlyResponse(HourOf(2m, actual, null)).Actual!;

        Assert.Equal(actual.Breakdown.Energy, response.Parts.Energy);
        Assert.Equal(actual.Breakdown.Markup, response.Parts.Markup);
        Assert.Equal(actual.Breakdown.Grid, response.Parts.Grid);
        Assert.Equal(actual.Breakdown.Taxes, response.Parts.Taxes);
        Assert.Equal(actual.Breakdown.Vat, response.Parts.Vat);
        Assert.True(response.Parts.GridSet);
    }

    [Fact]
    public void ThePartsAddUpToTheTotal_AndTheRateAndCostAreTheTotals()
    {
        RateCost actual = Rate(0.8007m, 0.0123m, 0.3041m, 1.5m);

        HourRateCostResponse response = CostMapper.ToHourlyResponse(HourOf(1.5m, actual, null)).Actual!;

        PriceParts p = response.Parts;
        Assert.Equal(p.Total, p.Energy + p.Markup + (p.Grid ?? 0m) + p.Taxes + p.Vat);
        Assert.Equal(p.Total, response.RatePerKwh);
        Assert.Equal(1.5m * p.Total, response.Cost);
    }

    [Fact]
    public void TheCurrentHourResponse_CarriesTheSameParts()
    {
        RateCost actual = Rate(1.00m, 0m, 0.20m, 0.5m);

        CurrentHourCostResponse response = CostMapper.ToCurrentHourResponse(
            Hour, 0.5m, new CostResult(PricingModel.NorgesPris, actual, null));

        Assert.Equal(actual.Breakdown.Total, response.Actual!.Parts.Total);
        Assert.Equal(response.Actual.Parts.Total, response.Actual.RatePerKwh);
        Assert.Null(response.Comparison);
    }

    [Fact]
    public void WithoutAGridTariff_TheGridPartIsNullAndMarkedNotSet()
    {
        RateCost actual = Rate(1.00m, 0.02m, null, 1m);

        PriceParts parts = CostMapper.ToHourlyResponse(HourOf(1m, actual, null)).Actual!.Parts;

        Assert.Null(parts.Grid);
        Assert.False(parts.GridSet);
    }

    [Fact]
    public void AnUnpricedHour_StillHasNoActualAndNoParts()
    {
        HourlyCostItemResponse item = CostMapper.ToHourlyResponse(HourOf(1m, null, null));

        Assert.Null(item.Actual);
        Assert.Null(item.Comparison);
    }

    [Fact]
    public void TheDailyCost_IsTheSumOfTheHourlyCosts()
    {
        HourCost[] hours =
        [
            HourOf(1m, Rate(1.00m, 0.02m, 0.30m, 1m), Rate(0.40m, 0m, 0.30m, 1m)),
            HourOf(2m, Rate(0.50m, 0.02m, 0.20m, 2m), Rate(0.40m, 0m, 0.20m, 2m))
        ];

        DailyCostItemResponse day = CostMapper.ToDailyResponse(new DateOnly(2026, 12, 2), hours);

        Assert.Equal(hours.Sum(h => h.Cost.Actual!.Cost), day.Actual!.Cost);
        Assert.Equal(hours.Sum(h => h.Cost.Comparison!.Cost), day.Comparison!.Cost);
    }

    [Fact]
    public async Task TheHourlyJson_HasAPartsObjectNextToTheTotals()
    {
        RateCost actual = Rate(1.00m, 0.02m, 0.30m, 2m);
        var response = new PagedHourlyCostResponse(
            [CostMapper.ToHourlyResponse(HourOf(2m, actual, null))], 1, 500, 1);
        await using EndpointHost host = await EndpointHost.StartAsync(dispatcher =>
            dispatcher
                .Send(Arg.Any<GetHourlyCostQuery>(), Arg.Any<CancellationToken>())
                .Returns(Result.Success(response)));

        string json = await host.Client.GetStringAsync($"/api/electricity-cost/hourly?locationId={Guid.NewGuid()}");

        Assert.Contains("\"ratePerKwh\"", json);
        Assert.Contains("\"cost\"", json);
        Assert.Contains("\"parts\":{", json);
        foreach (string name in new[] { "energy", "markup", "grid", "taxes", "vat", "total", "gridSet" })
        {
            Assert.Contains($"\"{name}\"", json);
        }
    }
}
