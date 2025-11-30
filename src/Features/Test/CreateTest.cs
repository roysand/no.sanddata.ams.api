using Domain.Common;
using FastEndpoints;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;

namespace Features.Test;



public static class CreateTest
{
    public record CreateTestResponse(string ResponseMessage);
    public record CreateTestRequest(string FirstName, string LastName, string Email) : IRequest<Result<CreateTestResponse>>;

    internal sealed class Handler : IRequestHandler<CreateTestRequest, Result<CreateTestResponse>>
    {
        public async Task<Result<CreateTestResponse>> Handle(CreateTestRequest request, CancellationToken cancellationToken)
        {
            var responseMessage = $"Test created for {request.FirstName} {request.LastName} with email {request.Email}.";
            return Result.Success<CreateTestResponse>(new CreateTestResponse(responseMessage));
        }
    }

    public class CreateTestValidator : FastEndpoints.Validator<CreateTestRequest>
    {
        public CreateTestValidator()
        {
            RuleFor(x => x.FirstName).NotEmpty().WithMessage("First name is required.");
            RuleFor(x => x.FirstName).Must(m => !m.Contains("old")).WithMessage("First name cannot contain 'old'.");
            RuleFor(x => x.LastName).NotEmpty().WithMessage("Last name is required.");
            RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("A valid email is required.");
        }
    }
}
public class CreateTestEndpoint(ISender sender) : FastEndpoints.Endpoint<CreateTest.CreateTestRequest, CreateTest.CreateTestResponse>
{
    public override void Configure()
    {
        Post("/api/create-test");
        AllowAnonymous();
    }

    public override async Task<CreateTest.CreateTestResponse> HandleAsync(CreateTest.CreateTestRequest req, CancellationToken ct)
    {
        var response = await sender.Send(req, ct);

        if (!response.IsSuccess)
        {
            AddError(response.Error.Code, response.Error.Description);
            ThrowIfAnyErrors();
        }

        return response.Value;
    }
}

