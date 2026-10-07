using Features.Locations.Endpoints;
using Features.Locations.Validators;

namespace Features.Tests.Locations;

public class CreateLocationValidatorTests
{
    private readonly CreateLocationValidator _validator = new();

    private static CreateLocationRequest Request(
        string name = "Cabin", string address = "Hyttevegen 1", string serial = "SN-1", string zone = "NO1") =>
        new(name, address, serial, zone);

    [Fact]
    public void Validate_ValidRequest_Passes() => Assert.True(_validator.Validate(Request()).IsValid);

    [Theory]
    [InlineData("NO1")]
    [InlineData("NO2")]
    [InlineData("NO3")]
    [InlineData("NO4")]
    [InlineData("NO5")]
    public void Validate_SupportedZones_Pass(string zone) => Assert.True(_validator.Validate(Request(zone: zone)).IsValid);

    [Theory]
    [InlineData("NO6")]
    [InlineData("SE3")]
    [InlineData("no1")]
    [InlineData("")]
    public void Validate_UnsupportedZone_FailsWithTheZoneErrorCode(string zone)
    {
        FluentValidation.Results.ValidationResult result = _validator.Validate(Request(zone: zone));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorCode == "Validation.InvalidZone");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyOrWhitespaceText_Fails(string text)
    {
        Assert.False(_validator.Validate(Request(name: text)).IsValid);
        Assert.False(_validator.Validate(Request(address: text)).IsValid);
        Assert.False(_validator.Validate(Request(serial: text)).IsValid);
    }

    [Fact]
    public void Validate_OverLongText_Fails()
    {
        string tooLong = new('x', 101);

        Assert.False(_validator.Validate(Request(name: tooLong)).IsValid);
        Assert.False(_validator.Validate(Request(address: tooLong)).IsValid);
        Assert.False(_validator.Validate(Request(serial: tooLong)).IsValid);
        Assert.True(_validator.Validate(Request(name: new string('x', 100))).IsValid);
    }
}
