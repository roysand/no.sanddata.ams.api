using FastEndpoints;
using Features.Users.Queries;
using FluentValidation;

namespace Features.Users.Validators;

public class GetUsersValidator : Validator<GetUsersQuery>
{
    public GetUsersValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThan(0).WithMessage("Page number must be greater than 0");

        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("Page size must be greater than 0")
            .LessThanOrEqualTo(100).WithMessage("Page size must not exceed 100");
    }
}
