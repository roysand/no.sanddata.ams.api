using Features.Users.Endpoints;
using Features.Users.Validators;

namespace Features.Tests.Users;

public class ChangePasswordValidatorTests
{
    private readonly ChangePasswordValidator _validator = new();

    [Fact]
    public void Validate_CurrentPasswordOmitted_Passes() =>
        Assert.True(_validator.Validate(new ChangePasswordRequest(Guid.NewGuid(), null, "NewPassw0rd!")).IsValid);

    [Fact]
    public void Validate_CurrentPasswordGiven_Passes() =>
        Assert.True(_validator.Validate(new ChangePasswordRequest(Guid.NewGuid(), "OldPassw0rd!", "NewPassw0rd!")).IsValid);

    [Fact]
    public void Validate_NewPasswordEqualsCurrent_Fails() =>
        Assert.False(_validator.Validate(new ChangePasswordRequest(Guid.NewGuid(), "SamePassw0rd!", "SamePassw0rd!")).IsValid);

    [Theory]
    [InlineData("Sh0rt!x")]
    [InlineData("alllowercase1!")]
    [InlineData("ALLUPPERCASE1!")]
    [InlineData("NoNumbersHere!")]
    [InlineData("NoSpecial1234")]
    public void Validate_WeakNewPassword_Fails(string newPassword) =>
        Assert.False(_validator.Validate(new ChangePasswordRequest(Guid.NewGuid(), null, newPassword)).IsValid);

    [Fact]
    public void Validate_EmptyId_Fails() =>
        Assert.False(_validator.Validate(new ChangePasswordRequest(Guid.Empty, null, "NewPassw0rd!")).IsValid);
}
