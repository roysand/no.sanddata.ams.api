using Application.WaterHeaters;
using Domain.Common.Entities;

namespace Features.Tests.WaterHeaters;

internal static class WaterHeaterTestData
{
    public static readonly Guid LocationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid UserId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static Location Location(bool active = true) =>
        new(LocationId, "Hjemme", "Street 1", "SN-1", "NO1", active, hasNorgesPriceAgreement: false);

    public static WaterHeater Heater(string deviceId = OsoSamples.DeviceId, Guid? locationId = null) =>
        new(Guid.NewGuid(), locationId ?? LocationId, deviceId, "v1:protected");

    public static WaterHeaterReading Reading(DateTime? readingTimeUtc = null) => new(
        ReadingTimeUtc: readingTimeUtc ?? new DateTime(2026, 11, 19, 18, 6, 48, 703, DateTimeKind.Utc),
        LastActivityUtc: null,
        IsOnline: true,
        IsHeating: false,
        SetPointCelsius: 72m,
        TemperatureTopCelsius: 72.8m,
        TemperatureMidCelsius: 72.3m,
        TemperatureLowCelsius: 70.5m,
        TemperatureFourthCelsius: null,
        AvailableLitres40: 344,
        MaxLitres40: 360,
        AvailableShowers: 3,
        AvailableBaths: 1,
        PowerKw: 0.011774m,
        StoredEnergyKwh: 13.97741m,
        SpecialMode: SpecialMode.Supercharge,
        SpecialModeStartUtc: new DateTime(2026, 11, 18, 19, 40, 14, DateTimeKind.Utc),
        SpecialModeEndUtc: new DateTime(2026, 11, 19, 19, 40, 14, DateTimeKind.Utc),
        ProfileName: "SuperchargeMode",
        OsoCodes: new OsoCodes(0, 1, 2, 3));
}
