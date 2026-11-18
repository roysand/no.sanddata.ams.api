using System.Globalization;
using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.ElectricityCost.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.ElectricityCost.Endpoints;

/// <param name="Month">yyyy-MM; the current Oslo month when omitted.</param>
public record GetMonthlyBillRequest(Guid LocationId, string? Month = null);

public class GetMonthlyBillEndpoint : Endpoint<GetMonthlyBillRequest, MonthlyBillResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetMonthlyBillEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/electricity-cost/bill");
        Tags("ElectricityCost");
        Description(b => b.WithTags("ElectricityCost"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Summary(s =>
        {
            s.Summary = "Get the electricity bill of a month, as the invoice shows it";
            s.Description = "Two parts, each with its lines and subtotal: Strom (supplier monthly fee, spot energy, " +
                             "supplier markup) and Nettleie (grid energy charge for weekday day kWh and for night and " +
                             "weekend kWh, capacity charge, electricity tax, Enova fee and, on Norgespris, the Norgespris " +
                             "credit). On Norgespris the supplier bills the spot price for every kWh and the grid company " +
                             "credits (spot - Norgespris) x (1 + VAT) per kWh up to the monthly cap, with 0 percent VAT on " +
                             "the credit. Amounts include VAT unless named excluding, and are not rounded. status is Final " +
                             "once the month has ended, Estimate before. A line with set false lacks a setting (monthly " +
                             "fee, grid tariff, capacity steps) and has null amounts, never zero; isComplete is false when " +
                             "a line is not set or not complete or some hours with readings could not be priced.";
            s.Response(200, "Bill retrieved successfully");
            s.Response(400, "Malformed month, or a month that has not started");
            s.Response(401, "Not signed in");
            s.Response(404, "Location not found");
        });
    }

    public override async Task HandleAsync(GetMonthlyBillRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        // The validator has checked the format; null means the current month, decided by the handler.
        DateOnly? month = req.Month is { } text
            ? DateOnly.ParseExact(text + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;

        Result<MonthlyBillResponse> result =
            await _dispatcher.Send(new GetMonthlyBillQuery(userId, req.LocationId, month), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(result.Error.Type == ErrorType.NotFound ? 404 : 400);
        }

        Response = result.Value;
    }
}
