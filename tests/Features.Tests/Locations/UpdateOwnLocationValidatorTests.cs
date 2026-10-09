using Features.Locations.Endpoints;
using Features.Locations.Validators;
using FluentValidation.Results;

namespace Features.Tests.Locations;

public class UpdateOwnLocationValidatorTests
{
    private readonly UpdateOwnLocationValidator _validator = new();

    private static UpdateOwnLocationRequest Valid() => new(Guid.NewGuid(), "Cabin", "Hyttevegen 1", true);

    [Fact]
    public void ValidRequest_Passes() => Assert.True(_validator.Validate(Valid()).IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NameAndAddress_AreRequired(string value)
    {
        Assert.False(_validator.Validate(Valid() with { Name = value }).IsValid);
        Assert.False(_validator.Validate(Valid() with { Address = value }).IsValid);
    }

    [Fact]
    public void NameAndAddress_AllowOneHundredCharacters_AndRejectMore()
    {
        Assert.True(_validator.Validate(Valid() with { Name = new string('x', 100), Address = new string('x', 100) }).IsValid);
        Assert.False(_validator.Validate(Valid() with { Name = new string('x', 101) }).IsValid);
        Assert.False(_validator.Validate(Valid() with { Address = new string('x', 101) }).IsValid);
    }

    [Fact]
    public void ActiveFlag_IsRequired_SoAMissingValueDoesNotSwitchTheLocationOff()
    {
        ValidationResult result = _validator.Validate(Valid() with { IsActive = null });

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "The active flag is required");
    }

    [Fact]
    public void LocationId_IsRequired() => Assert.False(_validator.Validate(Valid() with { Id = Guid.Empty }).IsValid);
}
