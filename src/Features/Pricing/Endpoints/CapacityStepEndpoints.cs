using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Pricing.Commands;
using Features.Pricing.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Pricing.Endpoints;

/// <param name="Id">The grid tariff, from the route.</param>
public record ListCapacityStepsRequest(Guid Id);

/// <param name="Id">The grid tariff, from the route.</param>
/// <param name="Steps">The whole table, lowest step first: starting at 0 kW, each step starting where the previous one
/// ends, and only the last one without an upper bound. Amounts are NOK per month excluding VAT.</param>
public record AddCapacityStepsRequest(
    Guid Id, DateOnly ValidFrom, IReadOnlyList<CapacityStepInput> Steps, bool ConfirmPastDate = false);

/// <param name="Id">The grid tariff, from the route.</param>
/// <param name="ValidFrom">The start date of the table, from the route (yyyy-MM-dd).</param>
public record DeleteCapacityStepsRequest(Guid Id, DateOnly ValidFrom);

public class ListCapacityStepsEndpoint : Endpoint<ListCapacityStepsRequest, IReadOnlyList<CapacityStepTableResponse>>
{
    private readonly IDispatcher _dispatcher;

    public ListCapacityStepsEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/admin/grid-tariffs/{id}/capacity-steps");
        Tags("Pricing");
        Description(b => b.WithTags("Pricing"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "List the capacity step tables of a grid tariff (admin)";
            s.Description = "Every step table of the tariff, oldest first, with its status (Superseded, InEffect, Upcoming). " +
                             "Amounts are NOK per month excluding VAT.";
            s.Response(200, "The tables");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "Grid tariff not found");
        });
    }

    public override async Task HandleAsync(ListCapacityStepsRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<IReadOnlyList<CapacityStepTableResponse>> result =
            await _dispatcher.Send(new ListCapacityStepsQuery(req.Id, userId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(PricingErrors.StatusOf(result.Error));
        }

        Response = result.Value;
    }
}

public class AddCapacityStepsEndpoint : Endpoint<AddCapacityStepsRequest, CapacityStepTableResponse>
{
    private readonly IDispatcher _dispatcher;

    public AddCapacityStepsEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Post("/api/admin/grid-tariffs/{id}/capacity-steps");
        Tags("Pricing");
        Description(b => b.WithTags("Pricing"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Add a capacity step table to a grid tariff (admin)";
            s.Description = "The monthly capacity charge steps from validFrom, as a whole table: the first step starts at 0 kW, " +
                             "each step covers from lowerKw up to but not including upperKw, the next starts where it ends, " +
                             "and only the last has upperKw null. monthlyAmount is NOK per month EXCLUDING VAT (divide a " +
                             "published amount that includes VAT by 1.25). A table applies to a whole month from the first " +
                             "month it has started by. A validFrom of today or earlier needs to start after every existing " +
                             "table of the tariff and confirmPastDate true.";
            s.Response(201, "Table added");
            s.Response(400, "Invalid start date or steps");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "Grid tariff not found");
            s.Response(409, "DateExists, NotNewest or ConfirmationRequired");
        });
    }

    public override async Task HandleAsync(AddCapacityStepsRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<CapacityStepTableResponse> result = await _dispatcher.Send(
            new AddCapacityStepsCommand(req.Id, req.ValidFrom, req.Steps, req.ConfirmPastDate, userId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(PricingErrors.StatusOf(result.Error));
        }

        HttpContext.Response.StatusCode = 201;
        HttpContext.Response.Headers.Location = $"/api/admin/grid-tariffs/{req.Id}/capacity-steps";
        Response = result.Value;
    }
}

public class DeleteCapacityStepsEndpoint : Endpoint<DeleteCapacityStepsRequest>
{
    private readonly IDispatcher _dispatcher;

    public DeleteCapacityStepsEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Delete("/api/admin/grid-tariffs/{id}/capacity-steps/{validFrom}");
        Tags("Pricing");
        Description(b => b.WithTags("Pricing"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Delete a capacity step table that has not started (admin)";
            s.Description = "Removes every step of the table. Only a table whose start date is after today can be deleted; " +
                             "add a newer table to correct one that has started.";
            s.Response(204, "Table deleted");
            s.Response(400, "Invalid start date");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "No table of this tariff starts on that date");
            s.Response(409, "The table has already started");
        });
    }

    public override async Task HandleAsync(DeleteCapacityStepsRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<CapacityStepsDeleted> result =
            await _dispatcher.Send(new DeleteCapacityStepsCommand(req.Id, req.ValidFrom, userId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(PricingErrors.StatusOf(result.Error));
        }

        HttpContext.Response.StatusCode = 204;
    }
}
