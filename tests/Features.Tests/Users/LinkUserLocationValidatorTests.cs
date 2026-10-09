using Features.Users.Endpoints;
using Features.Users.Validators;
using FluentValidation.Results;

namespace Features.Tests.Users;

public class LinkUserLocationValidatorTests
{
    private readonly LinkUserLocationValidator _validator = new();

    private static LinkUserLocationRequest Request(string? role) =>
        new() { Id = Guid.NewGuid(), LocationId = Guid.NewGuid(), Role = role };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Owner")]
    [InlineData("Viewer")]
    [InlineData("viewer")]
    [InlineData(" OWNER ")]
    public void Role_IsOptionalAndAcceptsTheTwoRoleNames(string? role) => Assert.True(_validator.Validate(Request(role)).IsValid);

    [Theory]
    [InlineData("Admin")]
    [InlineData("1")]
    [InlineData("Owner,Viewer")]
    [InlineData("Editor")]
    public void Role_RejectsAnythingElse(string role)
    {
        ValidationResult result = _validator.Validate(Request(role));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage == "Role must be Owner or Viewer");
    }
}
