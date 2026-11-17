using Application.ElectricityCost;

namespace Features.Tests.Pricing;

public class PriceCalculationTests
{
    [Fact]
    public void ASpotHour_IsTheFullPriceByHand()
    {
        // energy 0.80 + markup 0.02 + grid 0.30 + tax 0.0713 + Enova 0.01 = 1.2013; VAT 25 percent = 0.300325
        PriceBreakdown price = PriceCalculation.Calculate(0.80m, 0.02m, 0.30m, 0.0713m, 0.0100m, 0.25m);

        Assert.Equal(0.80m, price.Energy);
        Assert.Equal(0.02m, price.Markup);
        Assert.Equal(0.30m, price.Grid);
        Assert.Equal(0.0813m, price.Taxes);
        Assert.Equal(0.300325m, price.Vat);
        Assert.Equal(1.501625m, price.Total);
        Assert.True(price.GridSet);
    }

    [Fact]
    public void ANorgespris_Hour_IsTheFullPriceByHand()
    {
        // energy 0.40 + markup 0 + grid 0.30 + taxes 0.0813 = 0.7813; VAT = 0.195325; total 0.976625
        PriceBreakdown price = PriceCalculation.Calculate(0.40m, 0m, 0.30m, 0.0713m, 0.0100m, 0.25m);

        Assert.Equal(0.195325m, price.Vat);
        Assert.Equal(0.976625m, price.Total);
    }

    [Fact]
    public void WithNoGridTariff_TheGridPartIsLeftOutAndMarkedNotSet()
    {
        // 0.80 + 0.02 + 0.0813 = 0.9013; VAT = 0.225325; total 1.126625
        PriceBreakdown price = PriceCalculation.Calculate(0.80m, 0.02m, null, 0.0713m, 0.0100m, 0.25m);

        Assert.Null(price.Grid);
        Assert.False(price.GridSet);
        Assert.Equal(1.126625m, price.Total);
    }

    [Fact]
    public void WithNoVat_TheTotalIsTheNetPrice()
    {
        PriceBreakdown price = PriceCalculation.Calculate(0.80m, 0.02m, 0.30m, 0.0713m, 0.0100m, 0m);

        Assert.Equal(0m, price.Vat);
        Assert.Equal(1.2013m, price.Total);
    }

    [Theory]
    [InlineData(0.8007, 0.0123, 0.3041, 0.0713, 0.0100, 0.25)]
    [InlineData(0.0001, 0, 0.0001, 0.0001, 0.0001, 0.1234)]
    [InlineData(2.3456, 0.9999, 0.7777, 0.0713, 0.0100, 0.25)]
    public void ThePartsAddUpToTheTotalExactly(
        double energy, double markup, double grid, double tax, double enova, double vat)
    {
        PriceBreakdown price = PriceCalculation.Calculate(
            (decimal)energy, (decimal)markup, (decimal)grid, (decimal)tax, (decimal)enova, (decimal)vat);

        Assert.Equal(price.Total, price.Energy + price.Markup + (price.Grid ?? 0m) + price.Taxes + price.Vat);
    }

    [Fact]
    public void ThePartsAddUpToTheTotalWithoutAGridPart()
    {
        PriceBreakdown price = PriceCalculation.Calculate(0.7777m, 0.0123m, null, 0.0713m, 0.0100m, 0.25m);

        Assert.Equal(price.Total, price.Energy + price.Markup + price.Taxes + price.Vat);
    }
}
