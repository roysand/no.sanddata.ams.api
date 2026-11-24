using System.Globalization;
using System.Net;
using System.Text.Json;
using Application.Common.Interfaces.External;
using Application.WaterHeaters;
using Domain.Common;
using Infrastructure.External.Logging;
using Microsoft.Extensions.Logging;

namespace Infrastructure.External;

/// <summary>
/// Read-only client for OSO's inCharge API (https://api.osoenergy.no/water-heater-api-swagger/, spec 011).
/// What the parsing relies on, checked against the real heater on 2026-11-19:
/// - Some fields are misspelt (<c>holidyModeStartTime</c>), and casing differs between calls (<c>timeStamp</c> /
///   <c>timestamp</c>), so the DTOs keep OSO's spelling and matching is case-insensitive.
/// - The supercharge and holiday times come without a time zone; they are taken as UTC (spec 011 Assumptions).
/// - <c>temperatureOne</c> is 0 for a sensor the heater does not have, and becomes null.
/// The device id is in every URL path and the key in a header: this class never logs the URL, the exception or its
/// message, and its HttpClient is registered without the factory's request loggers (research R3).
/// </summary>
public class OsoWaterHeaterClient(HttpClient httpClient, ILogger<OsoWaterHeaterClient> logger) : IWaterHeaterClient
{
    private const string KeyHeader = "Ocp-Apim-Subscription-Key";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public async Task<Result<WaterHeaterReading>> GetStateAsync(
        WaterHeaterCredentials credentials, CancellationToken cancellationToken)
    {
        Task<Result<StatusDto>> status = GetAsync<StatusDto>(credentials, "Status", cancellationToken);
        Task<Result<TemperatureDto>> temperature =
            GetAsync<TemperatureDto>(credentials, "CurrentTemperature", cancellationToken);
        Task<Result<ActualLoadDto>> load = GetAsync<ActualLoadDto>(credentials, "ActualLoad", cancellationToken);
        Task<Result<TappingCapacityDto>> stored =
            GetAsync<TappingCapacityDto>(credentials, "TappingCapacityKwh", cancellationToken);

        await Task.WhenAll(status, temperature, load, stored);

        Error? error = FirstError(status.Result, temperature.Result, load.Result, stored.Result);
        if (error is not null)
        {
            return Result.Failure<WaterHeaterReading>(error);
        }

        Result<WaterHeaterReading> reading =
            ToReading(status.Result.Value, temperature.Result.Value, load.Result.Value, stored.Result.Value);
        if (reading.IsFailure)
        {
            OsoLogMessages.InvalidResponse(logger, credentials.LocationId, "Status");
        }

        return reading;
    }

    /// <remarks>
    /// The litres come from <c>Status.availableMix40Volume</c>, the number the state endpoint shows (spec 020, research
    /// R2). A failed or unusable status never costs the power: the sample keeps it, with the litres missing.
    /// </remarks>
    public async Task<Result<WaterHeaterSample>> GetSampleAsync(
        WaterHeaterCredentials credentials, CancellationToken cancellationToken)
    {
        Task<Result<ActualLoadDto>> load = GetAsync<ActualLoadDto>(credentials, "ActualLoad", cancellationToken);
        Task<Result<StatusDto>> status = GetAsync<StatusDto>(credentials, "Status", cancellationToken);

        await Task.WhenAll(load, status);

        if (load.Result.IsFailure)
        {
            return Result.Failure<WaterHeaterSample>(load.Result.Error);
        }

        decimal power = load.Result.Value.ActualLoad;
        if (status.Result.IsFailure)
        {
            return Result.Success(new WaterHeaterSample(power, null, status.Result.Error));
        }

        return status.Result.Value.AvailableMix40Volume is >= 0 and int litres
            ? Result.Success(new WaterHeaterSample(power, litres, null))
            : Result.Success(new WaterHeaterSample(power, null, WaterHeaterErrors.InvalidResponse));
    }

    private async Task<Result<T>> GetAsync<T>(WaterHeaterCredentials credentials, string call, CancellationToken ct)
        where T : class
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"1/Device/{Uri.EscapeDataString(credentials.DeviceId)}/{call}");
        request.Headers.Add(KeyHeader, credentials.SubscriptionKey);

        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                OsoLogMessages.RequestFailed(logger, credentials.LocationId, call, (int)response.StatusCode);
                return Result.Failure<T>(ErrorFor(response.StatusCode));
            }

            string body = await response.Content.ReadAsStringAsync(ct);
            T? value = JsonSerializer.Deserialize<T>(body, Json);
            if (value is null)
            {
                OsoLogMessages.InvalidResponse(logger, credentials.LocationId, call);
                return Result.Failure<T>(WaterHeaterErrors.InvalidResponse);
            }

            return Result.Success(value);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                   && !ct.IsCancellationRequested)
        {
            OsoLogMessages.Unavailable(logger, credentials.LocationId, call);
            return Result.Failure<T>(WaterHeaterErrors.Unavailable);
        }
        catch (JsonException)
        {
            OsoLogMessages.InvalidResponse(logger, credentials.LocationId, call);
            return Result.Failure<T>(WaterHeaterErrors.InvalidResponse);
        }
    }

    private static Error ErrorFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound => WaterHeaterErrors.Rejected,
        >= HttpStatusCode.InternalServerError => WaterHeaterErrors.Unavailable,
        HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests => WaterHeaterErrors.Unavailable,
        _ => WaterHeaterErrors.InvalidResponse
    };

    private static Error? FirstError(params Result[] results) =>
        results.FirstOrDefault(r => r.IsFailure)?.Error;

    private static Result<WaterHeaterReading> ToReading(
        StatusDto status, TemperatureDto temperature, ActualLoadDto load, TappingCapacityDto stored)
    {
        DateTime? readingTime = ParseUtc(status.LastTelemetry?.Timestamp) ?? ParseUtc(temperature.TimeStamp);
        if (readingTime is null)
        {
            return Result.Failure<WaterHeaterReading>(WaterHeaterErrors.InvalidResponse);
        }

        ProfileStatusDto? profile = status.ProfileStatus;
        (SpecialMode mode, DateTime? start, DateTime? end) = profile switch
        {
            { IsInHolidayMode: true } => (SpecialMode.Holiday,
                ParseUtc(profile.HolidyModeStartTime), ParseUtc(profile.HolidyModeEndTime)),
            { IsInSuperchargeMode: true } => (SpecialMode.Supercharge,
                ParseUtc(profile.SuperchargeModeStartTime), ParseUtc(profile.SuperchargeModeEndTime)),
            { IsInBlueBatteryMode: true } => (SpecialMode.BlueBattery, (DateTime?)null, (DateTime?)null),
            _ => (SpecialMode.None, (DateTime?)null, (DateTime?)null)
        };

        ControlDto? control = status.LastTelemetry?.Control;
        return Result.Success(new WaterHeaterReading(
            ReadingTimeUtc: readingTime.Value,
            LastActivityUtc: ParseUtc(status.LastActivityTime),
            IsOnline: status.ConnectionState == 1,
            IsHeating: control?.Heater == 1,
            SetPointCelsius: control?.CurrentSetPoint,
            TemperatureTopCelsius: temperature.TemperatureTop,
            TemperatureMidCelsius: temperature.TemperatureMid,
            TemperatureLowCelsius: temperature.TemperatureLow,
            TemperatureFourthCelsius: temperature.TemperatureOne is null or 0 ? null : temperature.TemperatureOne,
            AvailableLitres40: status.AvailableMix40Volume,
            MaxLitres40: status.MaxMix40Capacity,
            AvailableShowers: status.AvailableShowers,
            AvailableBaths: status.AvailableBaths,
            PowerKw: load.ActualLoad,
            StoredEnergyKwh: stored.TappingCapacitykWh,
            SpecialMode: mode,
            SpecialModeStartUtc: start,
            SpecialModeEndUtc: end,
            ProfileName: profile?.Meaning,
            OsoCodes: new OsoCodes(control?.Mode, status.OptimizationOption, status.OptimizationSubOption, profile?.Id)));
    }

    /// <summary>Times with a zone are converted to UTC; times without one are taken as UTC.</summary>
    private static DateTime? ParseUtc(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime parsed)
            ? parsed
            : null;

    // DTOs keep OSO's own spelling, including its typos.
    private sealed record StatusDto(
        TelemetryDto? LastTelemetry,
        int? OptimizationOption,
        int? OptimizationSubOption,
        int? AvailableMix40Volume,
        int? MaxMix40Capacity,
        int? ConnectionState,
        string? LastActivityTime,
        int? AvailableShowers,
        int? AvailableBaths,
        ProfileStatusDto? ProfileStatus);

    private sealed record TelemetryDto(string? Timestamp, ControlDto? Control);

    private sealed record ControlDto(int? Heater, int? Mode, decimal? CurrentSetPoint);

    private sealed record ProfileStatusDto(
        bool IsInHolidayMode,
        string? HolidyModeStartTime,
        string? HolidyModeEndTime,
        bool IsInSuperchargeMode,
        string? SuperchargeModeStartTime,
        string? SuperchargeModeEndTime,
        bool IsInBlueBatteryMode,
        int? Id,
        string? Meaning);

    private sealed record TemperatureDto(
        string? TimeStamp,
        decimal? TemperatureOne,
        decimal? TemperatureLow,
        decimal? TemperatureMid,
        decimal? TemperatureTop);

    private sealed record ActualLoadDto(decimal ActualLoad);

    private sealed record TappingCapacityDto(decimal? TappingCapacitykWh);
}
