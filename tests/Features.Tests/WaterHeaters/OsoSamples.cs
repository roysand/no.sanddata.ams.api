namespace Features.Tests.WaterHeaters;

/// <summary>
/// OSO's real answers for Roy's heater on 2026-11-19 (specs/_backlog/oso-water-heater.md), plus variants.
/// No real key or device id appears here; tests use <see cref="Key"/> and <see cref="DeviceId"/>.
/// </summary>
internal static class OsoSamples
{
    public const string Key = "TESTKEY-must-not-leak-0000";
    public const string DeviceId = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    public const string Status = """
        {"lastTelemetry":{"timestamp":"2026-11-19T18:06:48.703Z","control":{"heater":0,"mode":0,"currentSetPoint":72}},
         "optimizationOption":1,"optimizationSubOption":2,"smartCompanyId":null,
         "availableMix40Volume":344,"maxMix40Capacity":360,"connectionState":1,
         "lastActivityTime":"2026-11-19T18:06:48.703Z","availableShowers":3,"availableBaths":1,
         "profileStatus":{"isInHolidayMode":false,"holidyModeStartTime":null,"holidyModeEndTime":null,
           "isInSuperchargeMode":true,"superchargeModeStartTime":"2026-11-18T19:40:14.5919821",
           "superchargeModeEndTime":"2026-11-19T19:40:14.5919821","isInBlueBatteryMode":false,"id":3,
           "meaning":"SuperchargeMode"},
         "features":["SuperChargeMode"]}
        """;

    public const string StatusHeatingOnHoliday = """
        {"lastTelemetry":{"timestamp":"2026-11-19T18:06:48.703Z","control":{"heater":1,"mode":4,"currentSetPoint":55}},
         "optimizationOption":0,"optimizationSubOption":null,"availableMix40Volume":120,"maxMix40Capacity":360,
         "connectionState":0,"lastActivityTime":"2026-11-19T17:00:00Z","availableShowers":1,"availableBaths":0,
         "profileStatus":{"isInHolidayMode":true,"holidyModeStartTime":"2026-12-20T08:00:00Z",
           "holidyModeEndTime":"2026-12-27T08:00:00Z","isInSuperchargeMode":false,"superchargeModeStartTime":null,
           "superchargeModeEndTime":null,"isInBlueBatteryMode":false,"id":2,"meaning":"HolidayMode"},
         "features":[]}
        """;

    public const string CurrentTemperature =
        """{"timeStamp":"2026-11-19T18:06:48.703Z","temperatureOne":0,"temperatureLow":70.5,"temperatureMid":72.3,"temperatureTop":72.8}""";

    public const string CurrentTemperatureFourSensors =
        """{"timeStamp":"2026-11-19T18:06:48.703Z","temperatureOne":55.0,"temperatureLow":70.5,"temperatureMid":72.3,"temperatureTop":72.8}""";

    public const string ActualLoad = """{"actualLoad":0.011774}""";

    public const string TappingCapacityKwh = """{"tappingCapacitykWh":13.97741}""";
}
