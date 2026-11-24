using System.Net;
using Application.WaterHeaters;
using Domain.Common;
using Infrastructure.External;
using Microsoft.Extensions.Logging.Abstractions;

namespace Features.Tests.WaterHeaters;

public class OsoWaterHeaterClientTests
{
    private static readonly WaterHeaterCredentials Credentials =
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), OsoSamples.DeviceId, OsoSamples.Key);

    private static OsoWaterHeaterClient Client(OsoStubHandler handler, TimeSpan? timeout = null) =>
        new(handler.CreateClient(timeout), NullLogger<OsoWaterHeaterClient>.Instance);

    [Fact]
    public async Task GetStateAsync_MapsTheRealAnswers()
    {
        Result<WaterHeaterReading> result = await Client(new OsoStubHandler()).GetStateAsync(Credentials, default);

        Assert.True(result.IsSuccess);
        WaterHeaterReading r = result.Value;
        Assert.Equal(new DateTime(2026, 11, 19, 18, 6, 48, 703, DateTimeKind.Utc), r.ReadingTimeUtc);
        Assert.Equal(DateTimeKind.Utc, r.ReadingTimeUtc.Kind);
        Assert.True(r.IsOnline);
        Assert.False(r.IsHeating);
        Assert.Equal(72m, r.SetPointCelsius);
        Assert.Equal(72.8m, r.TemperatureTopCelsius);
        Assert.Equal(72.3m, r.TemperatureMidCelsius);
        Assert.Equal(70.5m, r.TemperatureLowCelsius);
        Assert.Null(r.TemperatureFourthCelsius);
        Assert.Equal(344, r.AvailableLitres40);
        Assert.Equal(360, r.MaxLitres40);
        Assert.Equal(3, r.AvailableShowers);
        Assert.Equal(1, r.AvailableBaths);
        Assert.Equal(0.011774m, r.PowerKw);
        Assert.Equal(13.97741m, r.StoredEnergyKwh);
        Assert.Equal(SpecialMode.Supercharge, r.SpecialMode);
        Assert.Equal(new DateTime(2026, 11, 18, 19, 40, 14, DateTimeKind.Utc).AddTicks(5919821), r.SpecialModeStartUtc);
        Assert.Equal(DateTimeKind.Utc, r.SpecialModeStartUtc!.Value.Kind);
        Assert.Equal(new DateTime(2026, 11, 19, 19, 40, 14, DateTimeKind.Utc).AddTicks(5919821), r.SpecialModeEndUtc);
        Assert.Equal("SuperchargeMode", r.ProfileName);
        Assert.Equal(new OsoCodes(0, 1, 2, 3), r.OsoCodes);
    }

    [Fact]
    public async Task GetStateAsync_KeepsARealFourthTemperature()
    {
        OsoStubHandler handler = new OsoStubHandler()
            .Answer("CurrentTemperature", HttpStatusCode.OK, OsoSamples.CurrentTemperatureFourSensors);

        Result<WaterHeaterReading> result = await Client(handler).GetStateAsync(Credentials, default);

        Assert.Equal(55.0m, result.Value.TemperatureFourthCelsius);
    }

    [Fact]
    public async Task GetStateAsync_ReadsHolidayModeHeatingAndOffline()
    {
        OsoStubHandler handler = new OsoStubHandler()
            .Answer("Status", HttpStatusCode.OK, OsoSamples.StatusHeatingOnHoliday);

        WaterHeaterReading r = (await Client(handler).GetStateAsync(Credentials, default)).Value;

        Assert.True(r.IsHeating);
        Assert.False(r.IsOnline);
        Assert.Equal(SpecialMode.Holiday, r.SpecialMode);
        Assert.Equal(new DateTime(2026, 12, 20, 8, 0, 0, DateTimeKind.Utc), r.SpecialModeStartUtc);
        Assert.Equal(new DateTime(2026, 12, 27, 8, 0, 0, DateTimeKind.Utc), r.SpecialModeEndUtc);
        Assert.Equal(new OsoCodes(4, 0, null, 2), r.OsoCodes);
    }

    [Fact]
    public async Task GetSampleAsync_ReadsPowerAndLitres()
    {
        var handler = new OsoStubHandler();

        Result<WaterHeaterSample> result = await Client(handler).GetSampleAsync(Credentials, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(new WaterHeaterSample(0.011774m, 344, null), result.Value);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains(handler.Requests, r => r.Path.EndsWith("/ActualLoad", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, r => r.Path.EndsWith("/Status", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "WaterHeater.Unavailable")]
    [InlineData(HttpStatusCode.Unauthorized, "WaterHeater.Rejected")]
    public async Task GetSampleAsync_StatusFails_KeepsThePower(HttpStatusCode status, string code)
    {
        OsoStubHandler handler = new OsoStubHandler().Answer("Status", status);

        Result<WaterHeaterSample> result = await Client(handler).GetSampleAsync(Credentials, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.011774m, result.Value.PowerKw);
        Assert.Null(result.Value.Litres40);
        Assert.Equal(code, result.Value.LitresError?.Code);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"availableMix40Volume":null}""")]
    [InlineData("""{"availableMix40Volume":-1}""")]
    [InlineData("not json")]
    public async Task GetSampleAsync_NoUsableLitres_KeepsThePower(string statusBody)
    {
        OsoStubHandler handler = new OsoStubHandler().Answer("Status", HttpStatusCode.OK, statusBody);

        Result<WaterHeaterSample> result = await Client(handler).GetSampleAsync(Credentials, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0.011774m, result.Value.PowerKw);
        Assert.Null(result.Value.Litres40);
        Assert.Equal("WaterHeater.InvalidResponse", result.Value.LitresError?.Code);
    }

    [Fact]
    public async Task GetSampleAsync_PowerFails_FailsTheSample()
    {
        OsoStubHandler handler = new OsoStubHandler().Answer("ActualLoad", HttpStatusCode.Unauthorized);

        Result<WaterHeaterSample> result = await Client(handler).GetSampleAsync(Credentials, default);

        Assert.Equal("WaterHeater.Rejected", result.Error.Code);
    }

    [Fact]
    public async Task Calls_UseTheDevicePathAndSendTheKeyHeader()
    {
        var handler = new OsoStubHandler();

        await Client(handler).GetStateAsync(Credentials, default);

        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal(OsoSamples.Key, r.Key));
        Assert.Contains(handler.Requests,
            r => r.Path == $"/water-heater-api/1/Device/{OsoSamples.DeviceId}/Status");
        Assert.Contains(handler.Requests,
            r => r.Path == $"/water-heater-api/1/Device/{OsoSamples.DeviceId}/TappingCapacityKwh");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "WaterHeater.Rejected")]
    [InlineData(HttpStatusCode.Forbidden, "WaterHeater.Rejected")]
    [InlineData(HttpStatusCode.NotFound, "WaterHeater.Rejected")]
    [InlineData(HttpStatusCode.InternalServerError, "WaterHeater.Unavailable")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "WaterHeater.Unavailable")]
    [InlineData(HttpStatusCode.BadRequest, "WaterHeater.InvalidResponse")]
    public async Task HttpErrors_AreMapped(HttpStatusCode status, string code)
    {
        OsoStubHandler handler = new OsoStubHandler().Answer("ActualLoad", status, """{"error":"x"}""");

        Result<WaterHeaterSample> load = await Client(handler).GetSampleAsync(Credentials, default);
        Result<WaterHeaterReading> state = await Client(handler).GetStateAsync(Credentials, default);

        Assert.Equal(code, load.Error.Code);
        Assert.Equal(code, state.Error.Code);
    }

    [Fact]
    public async Task ConnectionFailure_IsUnavailable()
    {
        var handler = new OsoStubHandler { Throw = new HttpRequestException("boom") };

        Result<WaterHeaterReading> result = await Client(handler).GetStateAsync(Credentials, default);

        Assert.Equal("WaterHeater.Unavailable", result.Error.Code);
    }

    [Fact]
    public async Task Timeout_IsUnavailableNotAnException()
    {
        var handler = new OsoStubHandler { Delay = TimeSpan.FromSeconds(5) };

        Result<WaterHeaterSample> result = await Client(handler, TimeSpan.FromMilliseconds(50))
            .GetSampleAsync(Credentials, default);

        Assert.Equal("WaterHeater.Unavailable", result.Error.Code);
    }

    [Fact]
    public async Task MalformedJson_IsInvalidResponse()
    {
        OsoStubHandler handler = new OsoStubHandler().Answer("Status", HttpStatusCode.OK, "not json");

        Result<WaterHeaterReading> result = await Client(handler).GetStateAsync(Credentials, default);

        Assert.Equal("WaterHeater.InvalidResponse", result.Error.Code);
    }

    [Fact]
    public async Task Errors_NeverContainTheKeyOrDeviceId()
    {
        OsoStubHandler handler = new OsoStubHandler().Answer("Status", HttpStatusCode.Unauthorized);

        Result<WaterHeaterReading> result = await Client(handler).GetStateAsync(Credentials, default);

        Assert.DoesNotContain(OsoSamples.Key, result.Error.Description);
        Assert.DoesNotContain(OsoSamples.DeviceId, result.Error.Description);
        Assert.DoesNotContain(OsoSamples.Key, Credentials.ToString());
        Assert.DoesNotContain(OsoSamples.DeviceId, Credentials.ToString());
    }
}
