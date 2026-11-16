using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Pricing.Commands;
using Features.Pricing.Mappers;
using Features.Pricing.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Pricing.Endpoints;

/// <summary>The status an error answers with, as the other admin endpoints map it.</summary>
internal static class PricingErrors
{
    public static int StatusOf(Error error) => error.Type switch
    {
        ErrorType.NotFound => 404,
        ErrorType.Conflict => 409,
        _ => 400
    };
}

/// <param name="Kind">Optional: ElectricityTax, EnovaFee or Vat.</param>
public record ListPriceRatesRequest(string? Kind = null);

/// <param name="Kind">ElectricityTax, EnovaFee or Vat. Value is NOK per kWh for the two fees and a fraction for VAT.</param>
public record AddPriceRateRequest(string Kind, DateOnly ValidFrom, decimal Value, bool ConfirmPastDate = false);

public class ListPriceRatesEndpoint : Endpoint<ListPriceRatesRequest, IReadOnlyList<PriceRateResponse>>
{
    private readonly IDispatcher _dispatcher;

    public ListPriceRatesEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/admin/price-rates");
        Tags("Pricing");
        Description(b => b.WithTags("Pricing"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "List the national price rates (admin)";
            s.Description = "Electricity tax and Enova fee in NOK per kWh and VAT as a fraction, each with a start date, " +
                             "oldest first. Status is Superseded, InEffect (one per kind) or Upcoming. Filter with kind.";
            s.Response(200, "Rates retrieved successfully");
            s.Response(400, "Unknown kind");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
        });
    }

    public override async Task HandleAsync(ListPriceRatesRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        PriceRateKind? kind = PricingMapper.TryParseKind(req.Kind, out PriceRateKind parsed) ? parsed : null;

        Result<IReadOnlyList<PriceRateResponse>> result = await _dispatcher.Send(new ListPriceRatesQuery(kind, userId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(PricingErrors.StatusOf(result.Error));
        }

        Response = result.Value;
    }
}

public class AddPriceRateEndpoint : Endpoint<AddPriceRateRequest, PriceRateResponse>
{
    private readonly IDispatcher _dispatcher;

    public AddPriceRateEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Post("/api/admin/price-rates");
        Tags("Pricing");
        Description(b => b.WithTags("Pricing"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Add a national price rate (admin)";
            s.Description = "Applies from validFrom (a date in Norway) until a later row of the same kind takes over. A " +
                             "validFrom of today or earlier is accepted only when it starts after every existing row of " +
                             "that kind and confirmPastDate is true, because costs shown for past hours are recalculated.";
            s.ExampleRequest = new AddPriceRateRequest("ElectricityTax", new DateOnly(2027, 1, 1), 0.0713m);
            s.Response(201, "Rate added");
            s.Response(400, "Invalid kind, start date or value");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(409, "DateExists, NotNewest or ConfirmationRequired");
        });
    }

    public override async Task HandleAsync(AddPriceRateRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        PricingMapper.TryParseKind(req.Kind, out PriceRateKind kind); // the validator has already refused anything else

        Result<PriceRateResponse> result = await _dispatcher.Send(
            new AddPriceRateCommand(kind, req.ValidFrom, req.Value, req.ConfirmPastDate, userId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(PricingErrors.StatusOf(result.Error));
        }

        HttpContext.Response.StatusCode = 201;
        HttpContext.Response.Headers.Location = "/api/admin/price-rates";
        Response = result.Value;
    }
}

public class DeletePriceRateEndpoint : EndpointWithoutRequest
{
    private readonly IDispatcher _dispatcher;

    public DeletePriceRateEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Delete("/api/admin/price-rates/{id}");
        Tags("Pricing");
        Description(b => b.WithTags("Pricing"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Delete a national price rate that has not started (admin)";
            s.Description = "Only a row whose start date is after today can be deleted. A row that has started, including " +
                             "the one in effect, cannot be changed or deleted; add a newer row instead.";
            s.Response(204, "Rate deleted");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "Rate not found");
            s.Response(409, "The rate has already started");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<PricingDeleted> result = await _dispatcher.Send(new DeletePriceRateCommand(Route<Guid>("id"), userId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(PricingErrors.StatusOf(result.Error));
        }

        HttpContext.Response.StatusCode = 204;
    }
}
