using FastEndpoints;
using FluentValidation;
using MediatR;

namespace Features.Test;



public static class CreateTest
{
    public record CreateTestResponse(string ResponseMessage);
    public record CreateTestRequest(string FirstName, string LastName, string Email) : IRequest<CreateTestResponse>;

    internal sealed class Handler : IRequestHandler<CreateTestRequest, CreateTestResponse>
    {
        public async Task<CreateTestResponse> Handle(CreateTestRequest request, CancellationToken cancellationToken)
        {
            // Simulate some processing logic
            await Task.Delay(100, cancellationToken);

            var responseMessage = $"Test created for {request.FirstName} {request.LastName} with email {request.Email}.";
            return new CreateTestResponse(responseMessage);
        }
    }

    public class CreateTestValidator : FastEndpoints.Validator<CreateTestRequest>
    {
        public CreateTestValidator()
        {
            RuleFor(x => x.FirstName).NotEmpty().WithMessage("First name is required.");
            RuleFor(x => x.LastName).NotEmpty().WithMessage("Last name is required.");
            RuleFor(x => x.Email).NotEmpty().EmailAddress().WithMessage("A valid email is required.");
        }
    }
}
public class CreateTestEndpoint(ISender sender) : FastEndpoints.Endpoint<CreateTest.CreateTestRequest>
{
    public override void Configure()
    {
        Post("/api/create-test");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CreateTest.CreateTestRequest req, CancellationToken ct)
    {
        // Simulate some processing logic
        await Task.Delay(100, ct);
        var response = await sender.Send(req, ct);

        await Send.OkAsync(new { Message = response }, cancellation: ct);
    }
}

