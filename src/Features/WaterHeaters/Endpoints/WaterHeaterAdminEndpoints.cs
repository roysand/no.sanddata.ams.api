using System.Globalization;
using System.Security.Claims;
using Application.CQRS;
using Domain.Common;
using FastEndpoints;
using Features.WaterHeaters.Commands;
using Features.WaterHeaters.Queries;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

namespace Features.WaterHeaters.Endpoints;

/// <summary>HTTP status of a water heater error (contracts/endpoints.md).</summary>
public static class WaterHeaterErrorStatus
{
    /// <param name="error">The failed result's error.</param>
    /// <param name="onConnect">
    /// True on connect, where a rejected key is the caller's bad input (400); elsewhere the stored key has gone bad at
    /// OSO's end, an upstream failure (502).
    /// </param>
    public static int Of(Error error, bool onConnect) => error.Code switch
    {
        "WaterHeater.Rejected" => onConnect ? 400 : 502,
        "WaterHeater.InvalidResponse" or "WaterHeater.KeyUnreadable" => 502,
        "WaterHeater.Unavailable" or "WaterHeater.EncryptionNotConfigured" => 503,
        _ => error.Type switch
        {
            ErrorType.NotFound => 404,
            ErrorType.Conflict => 409,
            _ => 400
        }
    };
}

/// <param name="LocationId">The location, from the route.</param>
/// <param name="SubscriptionKey">The OSO subscription key. Stored encrypted, never returned or logged.</param>
/// <param name="DeviceId">The heater's device id (hexadecimal) from the OSO app or website.</param>
/// <param name="KeyExpiresOn">
/// The last day the OSO key is valid, yyyy-MM-dd (spec 015). Optional: one year from today when left out.
/// </param>
public record ConnectWaterHeaterRequest(
    Guid LocationId, string SubscriptionKey, string DeviceId, string? KeyExpiresOn = null)
{
    public override string ToString() => $"ConnectWaterHeaterRequest(LocationId={LocationId})";
}

/// <summary>The OSO key's expiry date as sent: yyyy-MM-dd, checked by the validators before it is parsed here.</summary>
public static class KeyExpiryDate
{
    public const string Format = "yyyy-MM-dd";

    public static bool IsValid(string? text) =>
        text is not null && DateOnly.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static DateOnly Parse(string text) => DateOnly.ParseExact(text, Format, CultureInfo.InvariantCulture);
}

public class ConnectWaterHeaterEndpoint : Endpoint<ConnectWaterHeaterRequest, WaterHeaterSettingsResponse>
{
    private readonly IDispatcher _dispatcher;

    public ConnectWaterHeaterEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Put("/api/admin/locations/{locationId}/water-heater");
        Tags("WaterHeater");
        Description(b => b.WithTags("WaterHeater"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Connect or replace the OSO water heater of a location (admin)";
            s.Description = "Checks the subscription key and device id with OSO before saving. The key is stored " +
                             "encrypted and is never returned; the device id is shown only shortened. Connecting " +
                             "again replaces the old key and device id. keyExpiresOn (yyyy-MM-dd, " +
                             "optional) is the key's last valid day, one year from today when left out; it is " +
                             "checked before OSO is called. The request body is never logged.";
            s.Response(200, "Connected");
            s.Response(400, "Missing or invalid key, device id or expiry date, OSO rejected them " +
                            "(WaterHeater.Rejected), or the date is not after today or more than five years ahead " +
                            "(WaterHeater.KeyExpiryOutOfRange)");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "Location not found");
            s.Response(409, "The device is connected to another location (WaterHeater.DeviceInUse)");
            s.Response(502, "OSO answered in an unexpected way (WaterHeater.InvalidResponse)");
            s.Response(503, "OSO unavailable (WaterHeater.Unavailable), or no encryption key on the server " +
                            "(WaterHeater.EncryptionNotConfigured)");
        });
    }

    public override async Task HandleAsync(ConnectWaterHeaterRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        DateOnly? expiresOn = req.KeyExpiresOn is { } text ? KeyExpiryDate.Parse(text) : null;
        Result<WaterHeaterSettingsResponse> result = await _dispatcher.Send(
            new ConnectWaterHeaterCommand(userId, req.LocationId, req.SubscriptionKey, req.DeviceId, expiresOn), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(WaterHeaterErrorStatus.Of(result.Error, onConnect: true));
        }

        Response = result.Value;
    }
}

public class GetWaterHeaterSettingsEndpoint : EndpointWithoutRequest<WaterHeaterSettingsResponse>
{
    private readonly IDispatcher _dispatcher;

    public GetWaterHeaterSettingsEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Get("/api/admin/locations/{locationId}/water-heater");
        Tags("WaterHeater");
        Description(b => b.WithTags("WaterHeater"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Read the water heater connection of a location (admin)";
            s.Description = "The shortened device id, when it was connected and last changed, and the last day its OSO " +
                             "key is valid. Does not call OSO.";
            s.Response(200, "Connection found");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "Location not found, or no water heater connected (WaterHeater.NotConnected)");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        Result<WaterHeaterSettingsResponse> result =
            await _dispatcher.Send(new GetWaterHeaterSettingsQuery(Route<Guid>("locationId")), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(WaterHeaterErrorStatus.Of(result.Error, onConnect: false));
        }

        Response = result.Value;
    }
}

public class RemoveWaterHeaterEndpoint : EndpointWithoutRequest
{
    private readonly IDispatcher _dispatcher;

    public RemoveWaterHeaterEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Delete("/api/admin/locations/{locationId}/water-heater");
        Tags("WaterHeater");
        Description(b => b.WithTags("WaterHeater"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Remove the water heater connection of a location (admin)";
            s.Description = "Deletes the key and device id and stops sampling. Power samples already stored are kept.";
            s.Response(204, "Removed");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "Location not found, or no water heater connected (WaterHeater.NotConnected)");
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<WaterHeaterRemoved> result =
            await _dispatcher.Send(new RemoveWaterHeaterCommand(userId, Route<Guid>("locationId")), ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(WaterHeaterErrorStatus.Of(result.Error, onConnect: false));
        }

        HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }
}

/// <param name="LocationId">The location, from the route.</param>
/// <param name="KeyExpiresOn">The OSO key's new last valid day, yyyy-MM-dd.</param>
public record SetWaterHeaterKeyExpiryRequest(Guid LocationId, string KeyExpiresOn);

public class SetWaterHeaterKeyExpiryEndpoint : Endpoint<SetWaterHeaterKeyExpiryRequest, WaterHeaterSettingsResponse>
{
    private readonly IDispatcher _dispatcher;

    public SetWaterHeaterKeyExpiryEndpoint(IDispatcher dispatcher) => _dispatcher = dispatcher;

    public override void Configure()
    {
        Put("/api/admin/locations/{locationId}/water-heater/key-expiry");
        Tags("WaterHeater");
        Description(b => b.WithTags("WaterHeater"));
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        Roles(RoleNames.Admin);
        Summary(s =>
        {
            s.Summary = "Change the expiry date of a water heater's OSO key (admin)";
            s.Description = "Changes only the last day the OSO key is valid, for example after a typo or when OSO " +
                             "extends the key. The key and the device id are not needed, and OSO is not called.";
            s.Response(200, "Changed");
            s.Response(400, "Missing or malformed date, or not after today or more than five years ahead " +
                            "(WaterHeater.KeyExpiryOutOfRange)");
            s.Response(401, "Not signed in");
            s.Response(403, "Administrator role required");
            s.Response(404, "No water heater connected to this location (WaterHeater.NotConnected)");
        });
    }

    public override async Task HandleAsync(SetWaterHeaterKeyExpiryRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        Result<WaterHeaterSettingsResponse> result = await _dispatcher.Send(
            new SetWaterHeaterKeyExpiryCommand(userId, req.LocationId, KeyExpiryDate.Parse(req.KeyExpiresOn)),
            ct);

        if (!result.IsSuccess)
        {
            AddError(result.Error.Description, result.Error.Code);
            ThrowIfAnyErrors(WaterHeaterErrorStatus.Of(result.Error, onConnect: false));
        }

        Response = result.Value;
    }
}
