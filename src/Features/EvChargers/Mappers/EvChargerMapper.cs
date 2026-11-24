using Application.EvChargers;
using Domain.Common.Entities;
using Features.EvChargers.Queries;

namespace Features.EvChargers.Mappers;

public static class EvChargerMapper
{
    public static ZaptecChargerResponse ToResponse(ZaptecCharger charger) =>
        new(charger.Id, charger.Name, charger.InstallationId, charger.InstallationName);

    public static EvChargerSettingsResponse ToSettings(EvCharger charger, bool includeInTotals) =>
        new(
            charger.LocationId,
            charger.ChargerId,
            charger.ChargerName,
            charger.InstallationId,
            charger.InstallationName,
            charger.CreatedAt,
            charger.ReadUpToUtc,
            includeInTotals);

    public static ChargingSessionResponse ToResponse(ChargingSession session) =>
        new(
            DateTime.SpecifyKind(session.StartUtc, DateTimeKind.Utc),
            DateTime.SpecifyKind(session.EndUtc, DateTimeKind.Utc),
            session.Kwh,
            !session.HasEnergyDetails);

    public static ChargerStateResponse ToStateResponse(ChargerLiveState state) =>
        new(
            state.State,
            state.Online,
            state.PowerKw,
            state.SessionKwh,
            Utc(state.PluggedInAtUtc),
            Utc(state.ChargingSinceUtc),
            Utc(state.LastReportedAtUtc),
            Utc(state.ReadAtUtc));

    private static DateTime? Utc(DateTime? value) =>
        value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
}
