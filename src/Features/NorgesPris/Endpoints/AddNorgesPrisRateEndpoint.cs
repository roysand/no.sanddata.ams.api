using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.NorgesPris.Commands;
using Features.NorgesPris.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.NorgesPris.Endpoints;

public class AddNorgesPrisRateEndpoint : Endpoint<AddNorgesPrisRateRequest, NorgesPrisRateResponse>
{
    private readonly IDispatcher _dispatcher;

    public AddNorgesPrisRateEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Post("/api/admin/norgespris");
        Tags("NorgesPris");
        Description(b => b.WithTags("NorgesPris"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Add a Norgespris rate (admin)";
            s.Description = "The rate applies from validFrom (a date in Norway) until a later-dated rate takes over. A " +
                             "validFrom of today or earlier is accepted only when it starts after every existing rate " +
                             "and confirmPastDate is true, because costs shown for past hours are recalculated.";
            s.ExampleRequest = new AddNorgesPrisRateRequest(new DateOnly(2027, 1, 1), 0.50m, 0m);
            s.Response(201, "Rate added");
            s.Response(400, "Invalid start date, rate or tax");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(409, "DateExists, NotNewest or ConfirmationRequired");
        });
    }

    public override async Task HandleAsync(AddNorgesPrisRateRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<NorgesPrisRateResponse> result = await _dispatcher.Send(
            new AddNorgesPrisRateCommand(req.ValidFrom, req.RatePerKwh, req.TaxPerKwh, req.ConfirmPastDate, userId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type switch
            {
                ErrorType.Conflict => 409,
                _ => 400
            });
        }

        HttpContext.Response.StatusCode = 201;
        HttpContext.Response.Headers.Location = "/api/admin/norgespris";
        Response = result.Value;
    }
}

public record AddNorgesPrisRateRequest(
    DateOnly ValidFrom, decimal RatePerKwh, decimal TaxPerKwh, bool ConfirmPastDate = false);
