using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Pricing.Commands;
using Features.Pricing.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Pricing.Endpoints;

public record CreateGridTariffRequest(string Name);

/// <param name="Id">The grid tariff, from the route.</param>
public record AddGridTariffRateRequest(
    Guid Id, DateOnly ValidFrom, decimal DayRatePerKwh, decimal NightRatePerKwh, bool ConfirmPastDate = false);

public class ListGridTariffsEndpoint : EndpointWithoutRequest<IReadOnlyList<GridTariffResponse>>
{
    private readonly IDispatcher _dispatcher;

    public ListGridTariffsEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/admin/grid-tariffs");
        Tags("Pricing");
        Description(b => b.WithTags("Pricing"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "List the grid tariffs with their rates (admin)";
            s.Description = "Every tariff with its day rate and night and weekend rate per start date, oldest first, in NOK " +
                             "per kWh excluding taxes and VAT. Status is Superseded, InEffect (one per tariff) or Upcoming.";
            s.Response(200, "Tariffs retrieved successfully");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<IReadOnlyList<GridTariffResponse>> result = await _dispatcher.Send(new ListGridTariffsQuery(userId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(PricingErrors.StatusOf(result.Error));
        }

        Response = result.Value;
    }
}

public class CreateGridTariffEndpoint : Endpoint<CreateGridTariffRequest, GridTariffResponse>
{
    private readonly IDispatcher _dispatcher;

    public CreateGridTariffEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Post("/api/admin/grid-tariffs");
        Tags("Pricing");
        Description(b => b.WithTags("Pricing"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Create a grid tariff (admin)";
            s.Description = "Creates an empty tariff. Add its rates with POST /api/admin/grid-tariffs/{id}/rates, then set it " +
                             "on a location with PUT /api/admin/location-pricing/{locationId}.";
            s.ExampleRequest = new CreateGridTariffRequest("Elvia standard, household");
            s.Response(201, "Tariff created");
            s.Response(400, "Name missing or longer than 100 characters");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(409, "Another tariff already has this name");
        });
    }

    public override async Task HandleAsync(CreateGridTariffRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<GridTariffResponse> result = await _dispatcher.Send(new CreateGridTariffCommand(req.Name, userId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(PricingErrors.StatusOf(result.Error));
        }

        HttpContext.Response.StatusCode = 201;
        HttpContext.Response.Headers.Location = "/api/admin/grid-tariffs";
        Response = result.Value;
    }
}

public class AddGridTariffRateEndpoint : Endpoint<AddGridTariffRateRequest, GridTariffRateResponse>
{
    private readonly IDispatcher _dispatcher;

    public AddGridTariffRateEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Post("/api/admin/grid-tariffs/{id}/rates");
        Tags("Pricing");
        Description(b => b.WithTags("Pricing"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Add a rate to a grid tariff (admin)";
            s.Description = "The day rate (weekdays 06:00 to 22:00 Oslo time, public holidays excluded) and the night and " +
                             "weekend rate, in NOK per kWh EXCLUDING taxes and VAT: convert a published rate that includes " +
                             "them first, or the taxes are counted twice. Applies from validFrom until a later rate takes " +
                             "over. A validFrom of today or earlier needs to start after every existing rate of the tariff " +
                             "and confirmPastDate true, because past costs are recalculated.";
            s.Response(201, "Rate added");
            s.Response(400, "Invalid start date or rate");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "Grid tariff not found");
            s.Response(409, "DateExists, NotNewest or ConfirmationRequired");
        });
    }

    public override async Task HandleAsync(AddGridTariffRateRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<GridTariffRateResponse> result = await _dispatcher.Send(
            new AddGridTariffRateCommand(
                req.Id, req.ValidFrom, req.DayRatePerKwh, req.NightRatePerKwh, req.ConfirmPastDate, userId),
            ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(PricingErrors.StatusOf(result.Error));
        }

        HttpContext.Response.StatusCode = 201;
        HttpContext.Response.Headers.Location = "/api/admin/grid-tariffs";
        Response = result.Value;
    }
}

public class DeleteGridTariffRateEndpoint : EndpointWithoutRequest
{
    private readonly IDispatcher _dispatcher;

    public DeleteGridTariffRateEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Delete("/api/admin/grid-tariffs/{id}/rates/{rateId}");
        Tags("Pricing");
        Description(b => b.WithTags("Pricing"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Delete a grid tariff rate that has not started (admin)";
            s.Description = "Only a rate whose start date is after today can be deleted; add a newer rate to correct one that " +
                             "has started.";
            s.Response(204, "Rate deleted");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "Rate not found under this tariff");
            s.Response(409, "The rate has already started");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<PricingDeleted> result = await _dispatcher.Send(
            new DeleteGridTariffRateCommand(Route<Guid>("id"), Route<Guid>("rateId"), userId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(PricingErrors.StatusOf(result.Error));
        }

        HttpContext.Response.StatusCode = 204;
    }
}
