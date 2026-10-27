using Features.Meters.Endpoints;
using Features.Meters.Validators;
using FluentValidation.Results;

namespace Features.Tests.Meters;

public class UpdateMeterCommentValidatorTests
{
    private readonly UpdateMeterCommentValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Main building")]
    public void Comment_IsOptional(string? comment) =>
        Assert.True(_validator.Validate(new UpdateMeterCommentRequest(Guid.NewGuid(), comment)).IsValid);

    [Fact]
    public void Comment_AllowsTwoHundredCharacters_AndRejectsMore()
    {
        Assert.True(_validator.Validate(new UpdateMeterCommentRequest(Guid.NewGuid(), new string('x', 200))).IsValid);

        ValidationResult tooLong = _validator.Validate(new UpdateMeterCommentRequest(Guid.NewGuid(), new string('x', 201)));

        Assert.False(tooLong.IsValid);
        Assert.Contains(tooLong.Errors, e => e.ErrorMessage == "Comment must not exceed 200 characters");
    }

    [Fact]
    public void MeterId_IsRequired() =>
        Assert.False(_validator.Validate(new UpdateMeterCommentRequest(Guid.Empty, "x")).IsValid);
}
