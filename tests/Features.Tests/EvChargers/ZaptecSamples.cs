namespace Features.Tests.EvChargers;

/// <summary>
/// Zaptec answers shaped like the real ones for Roy's charger on 2026-11-20 (spec 016, research R2/R5). The first
/// session is Roy's real session of 2026-10-04 with its ids, names and personal fields replaced. No real sign-in,
/// token or id appears here; tests use <see cref="Username"/>, <see cref="Password"/> and <see cref="Token"/>.
/// </summary>
internal static class ZaptecSamples
{
    public const string Username = "test.user@example.invalid";
    public const string Password = "TESTPASSWORD-must-not-leak-0000";
    public const string Token = "TESTTOKEN-must-not-leak-0000";

    public static readonly Guid ChargerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid InstallationId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid FixtureSessionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid VoidedSessionId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    public static readonly Guid ReplacedSessionId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    public static readonly Guid NoDetailsSessionId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    public const string TokenAnswer = $$"""
        {"access_token":"{{Token}}","token_type":"Bearer","expires_in":86400,"scope":"openid"}
        """;

    // /api/chargers answers in PascalCase.
    public const string Chargers = """
        {"Pages":1,"TotalCount":1,"Data":[{"OperatingMode":1,"IsOnline":true,
          "Id":"22222222-2222-2222-2222-222222222222","MID":"ZAP000000","DeviceId":"ZAP000000","SerialNo":"Test charger",
          "Name":"Test charger","CreatedOnDate":"2021-05-01T10:00:00","CircuitId":"77777777-7777-7777-7777-777777777777",
          "Active":true,"CurrentUserRoles":3,"Pin":"0000","DeviceType":4,"InstallationName":"Test installation",
          "InstallationId":"33333333-3333-3333-3333-333333333333","AuthenticationType":0,
          "CurrentUserInstallationRoles":3,"IsAuthorizationRequired":false}]}
        """;

    /// <summary>The real session's energy per hour: the points add up to 33.982 kWh, the session's energy.</summary>
    public const string FixtureSession = """
        {"id":"11111111-1111-1111-1111-111111111111","externalId":null,"replacedBySessionId":null,
         "authorizedUser":null,"tokenName":null,"chargerId":"22222222-2222-2222-2222-222222222222",
         "deviceId":"ZAP000000","deviceName":"Test charger","chargerFirmwareVersion":"2.6.0.7","chargerDeviceType":4,
         "installationId":"33333333-3333-3333-3333-333333333333","installationName":"Test installation",
         "startDateTime":"2026-10-04T17:52:08.13+00:00","endDateTime":"2026-10-05T11:28:38.087+00:00",
         "recognizedDateTime":"2026-10-05T11:28:38.087+00:00","offline":false,"reliableClock":true,
         "stoppedByRfid":false,"signed":false,"voided":false,"aborted":false,"ocppNative":false,
         "externallyAbandoned":false,"energy":33.982,
         "energyDetails":[
           {"timestamp":"2026-10-04T17:52:08+00:00","energy":0},
           {"timestamp":"2026-10-04T18:00:00+00:00","energy":0.0020000000000095497},
           {"timestamp":"2026-10-04T20:00:00+00:00","energy":1.4099999999999966},
           {"timestamp":"2026-10-04T21:00:00+00:00","energy":4.526999999999987},
           {"timestamp":"2026-10-04T22:00:00+00:00","energy":4.52000000000001},
           {"timestamp":"2026-10-04T23:00:00+00:00","energy":4.521999999999991},
           {"timestamp":"2026-10-05T00:00:00+00:00","energy":4.52000000000001},
           {"timestamp":"2026-10-05T01:00:00+00:00","energy":4.5190000000000055},
           {"timestamp":"2026-10-05T02:00:00+00:00","energy":4.510999999999996},
           {"timestamp":"2026-10-05T02:59:59+00:00","energy":4.515999999999991},
           {"timestamp":"2026-10-05T04:00:00+00:00","energy":0.9350000000000023},
           {"timestamp":"2026-10-05T11:28:38+00:00","energy":0}]}
        """;

    /// <summary>Page 1: the real session, a voided one and a replaced one; more to come.</summary>
    public const string SessionsPage1 = $$"""
        {"sessions":[{{FixtureSession}},
          {"id":"44444444-4444-4444-4444-444444444444","replacedBySessionId":null,"voided":true,
           "chargerId":"22222222-2222-2222-2222-222222222222",
           "startDateTime":"2026-10-06T10:00:00+00:00","endDateTime":"2026-10-06T10:05:00+00:00","energy":0.1,
           "energyDetails":[]},
          {"id":"55555555-5555-5555-5555-555555555555","replacedBySessionId":"88888888-8888-8888-8888-888888888888",
           "voided":false,"chargerId":"22222222-2222-2222-2222-222222222222",
           "startDateTime":"2026-10-07T10:00:00+00:00","endDateTime":"2026-10-07T12:00:00+00:00","energy":5.0,
           "energyDetails":[]}],
         "cursor":"page-2","hasMore":true}
        """;

    /// <summary>Page 2: a session without per-hour energy, and one without an end (skipped).</summary>
    public const string SessionsPage2 = """
        {"sessions":[
          {"id":"66666666-6666-6666-6666-666666666666","replacedBySessionId":null,"voided":false,
           "chargerId":"22222222-2222-2222-2222-222222222222",
           "startDateTime":"2026-10-08T22:30:00+00:00","endDateTime":"2026-10-09T00:30:00+00:00","energy":8.0,
           "energyDetails":null},
          {"id":"99999999-9999-9999-9999-999999999999","replacedBySessionId":null,"voided":false,
           "chargerId":"22222222-2222-2222-2222-222222222222",
           "startDateTime":"2026-10-09T10:00:00+00:00","endDateTime":null,"energy":1.0,"energyDetails":[]}],
         "cursor":null,"hasMore":false}
        """;

    // State answers (spec 021, research R1): an array of { StateId, Timestamp, ValueAsString } (Zaptec also sends
    // the ChargerId, which isn't read), with timestamps in UTC without a "Z". Figures from Roy's Go during a charge on
    // 2026-11-24; ids and other values are made up. Provisional until spec 021 T002 confirms the format by hand.
    public const string StateSessionId = "88888888-8888-8888-8888-888888888888";
    public const string FakeSerial = "ZAP-FAKE-SERIAL-908";
    public const string FakeSim = "8947-FAKE-SIM-960";
    public const string FakeMeter = "OCMF|FAKE-SIGNED-METER-DATA-554";

    public const string StateCharging = $$"""
        [{"StateId":-2,"Timestamp":"2026-11-24T14:00:12.03","ValueAsString":"1"},
         {"StateId":-1,"Timestamp":"2026-11-24T18:33:10.5","ValueAsString":"0"},
         {"StateId":513,"Timestamp":"2026-11-24T17:54:02","ValueAsString":"4414.704"},
         {"StateId":553,"Timestamp":"2026-11-24T18:32:00","ValueAsString":"12.962"},
         {"StateId":710,"Timestamp":"2026-11-24T15:37:41","ValueAsString":"3"},
         {"StateId":721,"Timestamp":"2026-11-24T14:52:03","ValueAsString":"{{StateSessionId}}"},
         {"StateId":908,"Timestamp":"2026-11-01T10:00:00","ValueAsString":"{{FakeSerial}}"},
         {"StateId":960,"Timestamp":"2026-11-01T10:00:00","ValueAsString":"{{FakeSim}}"},
         {"StateId":554,"Timestamp":"2026-11-24T18:00:00","ValueAsString":"{{FakeMeter}}"}]
        """;

    public const string StateNoCar = """
        [{"StateId":-2,"Timestamp":"2026-11-24T14:00:12","ValueAsString":"1"},
         {"StateId":-1,"Timestamp":"2026-11-24T18:33:10","ValueAsString":"0"},
         {"StateId":710,"Timestamp":"2026-11-24T08:10:00","ValueAsString":"1"}]
        """;

    public const string StateUnreadable = """
        [{"StateId":-1,"Timestamp":"2026-11-24T18:33:10","ValueAsString":"0"},
         {"StateId":710,"Timestamp":"2026-11-24T15:37:41","ValueAsString":"x"},
         {"StateId":513,"Timestamp":"2026-11-24T17:54:02","ValueAsString":""}]
        """;
}
