using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.Pricing.Commands;
using Features.Pricing.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.Pricing.Endpoints;

/// <param name="LocationId">The location, from the route.</param>
/// <param name="GridTariffId">The grid tariff, or null to clear it.</param>
/// <param name="ElectricityTaxExempt">Optional; omitted keeps the current value.</param>
/// <param name="VatExempt">Optional; omitted keeps the current value.</param>
/// <param name="IsHolidayHome">Optional; omitted keeps the current value.</param>
/// <param name="SupplierMonthlyFee">Optional, NOK per month excluding VAT; omitted keeps the current value.</param>
public record SetLocationPricingRequest(
    Guid LocationId,
    Guid? GridTariffId,
    decimal SupplierMarkupPerKwh,
    bool? ElectricityTaxExempt = null,
    bool? VatExempt = null,
    bool? IsHolidayHome = null,
    decimal? SupplierMonthlyFee = null);

public class ListLocationPricingEndpoint : EndpointWithoutRequest<IReadOnlyList<LocationPricingResponse>>
{
    private readonly IDispatcher _dispatcher;

    public ListLocationPricingEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/admin/location-pricing");
        Tags("Pricing");
        Description(b => b.WithTags("Pricing"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "List the pricing settings of every location (admin)";
            s.Description = "The grid tariff and supplier markup of every location, including ones the caller is not linked " +
                             "to. isComplete is false for a location without a grid tariff (its prices leave the grid part out) or " +
                             "without a supplier monthly fee (its bill shows the fee as not set).";
            s.Response(200, "Locations retrieved successfully");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<IReadOnlyList<LocationPricingResponse>> result =
            await _dispatcher.Send(new ListLocationPricingQuery(userId), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(PricingErrors.StatusOf(result.Error));
        }

        Response = result.Value;
    }
}

public class SetLocationPricingEndpoint : Endpoint<SetLocationPricingRequest, LocationPricingResponse>
{
    private readonly IDispatcher _dispatcher;

    public SetLocationPricingEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Put("/api/admin/location-pricing/{locationId}");
        Tags("Pricing");
        Description(b => b.WithTags("Pricing"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary =
                "Set the grid tariff, supplier markup and monthly fee, exemptions and holiday-home flag of a location (admin)";
            s.Description = "supplierMarkupPerKwh is NOK per kWh excluding VAT, added on every kWh, Norgespris included. " +
                             "electricityTaxExempt (tiltakssonen), vatExempt (Nordland, Troms, Finnmark) and isHolidayHome " +
                             "(the lower Norgespris monthly cap) are optional; an omitted flag keeps its value. " +
                             "supplierMonthlyFee is NOK per month excluding VAT, a full month on every bill (0 for a " +
                             "supplier without a fee); optional, an omitted fee keeps its value. Changing " +
                             "any of them recalculates every past hour of the location, because costs are calculated from " +
                             "the current settings, as for the Norgespris agreement flag.";
            s.Response(200, "Settings changed");
            s.Response(400, "Invalid markup or monthly fee");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "Location or grid tariff not found");
        });
    }

    public override async Task HandleAsync(SetLocationPricingRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<LocationPricingResponse> result = await _dispatcher.Send(
            new SetLocationPricingCommand(
                req.LocationId,
                req.GridTariffId,
                req.SupplierMarkupPerKwh,
                req.ElectricityTaxExempt,
                req.VatExempt,
                req.IsHolidayHome,
                userId,
                req.SupplierMonthlyFee),
            ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(PricingErrors.StatusOf(result.Error));
        }

        Response = result.Value;
    }
}
