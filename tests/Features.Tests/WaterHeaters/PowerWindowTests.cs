using Features.WaterHeaters.Services;

namespace Features.Tests.WaterHeaters;

/// <summary>The window's defaults and limits (spec 017, research R3).</summary>
public class PowerWindowTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NothingGiven_IsTheLastThreeHours()
    {
        (DateTime from, DateTime to) = PowerWindow.Resolve(null, null, Now);

        Assert.Equal(Now.AddHours(-3), from);
        Assert.Equal(Now, to);
    }

    [Fact]
    public void OnlyFrom_EndsNow()
    {
        DateTime given = new(2026, 11, 20, 7, 0, 0, DateTimeKind.Utc);

        Assert.Equal((given, Now), PowerWindow.Resolve(given, null, Now));
    }

    [Fact]
    public void OnlyTo_StartsThreeHoursBeforeIt()
    {
        DateTime given = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal((given.AddHours(-3), given), PowerWindow.Resolve(null, given, Now));
    }

    [Fact]
    public void ALocalTime_IsConvertedToUtc()
    {
        DateTime local = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc).ToLocalTime();

        (DateTime from, DateTime to) = PowerWindow.Resolve(local, local.AddHours(1), Now);

        Assert.Equal(DateTimeKind.Utc, from.Kind);
        Assert.Equal(new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc), from);
        Assert.Equal(new DateTime(2026, 11, 20, 11, 0, 0, DateTimeKind.Utc), to);
    }

    [Fact]
    public void ATimeWithAnOffset_IsConvertedToUtc()
    {
        var parsed = DateTime.Parse("2026-11-20T10:00:00+01:00", System.Globalization.CultureInfo.InvariantCulture);

        (DateTime from, _) = PowerWindow.Resolve(parsed, null, Now);

        Assert.Equal(new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc), from);
    }

    [Fact]
    public void AValidWindow_HasNoProblem() =>
        Assert.Null(PowerWindow.Problem(Now.AddHours(-3), Now, Now));

    [Fact]
    public void FromNotBeforeTo_IsRefused()
    {
        Assert.Equal("from must be before to", PowerWindow.Problem(Now, Now, Now));
        Assert.Equal("from must be before to", PowerWindow.Problem(Now.AddHours(-1), Now.AddHours(-2), Now));
    }

    [Fact]
    public void ExactlyFortyEightHours_IsAllowed_AndOneSecondMoreIsNot()
    {
        Assert.Null(PowerWindow.Problem(Now.AddHours(-48), Now, Now));
        Assert.Equal("The window can be at most 48 hours",
            PowerWindow.Problem(Now.AddHours(-48).AddSeconds(-1), Now, Now));
    }

    [Fact]
    public void FromInTheFuture_IsRefused() =>
        Assert.Equal("from must not be in the future",
            PowerWindow.Problem(Now.AddMinutes(1), Now.AddHours(1), Now));

    [Fact]
    public void ToInTheFuture_WithFromInThePast_IsAllowed() =>
        Assert.Null(PowerWindow.Problem(Now.AddHours(-1), Now.AddHours(2), Now));
}
