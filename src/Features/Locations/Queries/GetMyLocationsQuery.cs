using Application.CQRS;
using Domain.Common;
using Features.Meters.Commands;

namespace Features.Locations.Queries;

public record GetMyLocationsQuery(Guid UserId) : IQuery<Result<IReadOnlyList<LocationSummaryResponse>>>;

/// <param name="Role">"Owner" or "Viewer"; as text, because the API does not serialise enums by name.</param>
/// <param name="IsActive">Owners also receive their inactive locations so they can switch them on again.</param>
/// <param name="HasWaterHeater">True when an OSO water heater is connected, so the app can show its tab at once without
/// reading the heater.</param>
/// <param name="HasEvCharger">True when a Zaptec EV charger is connected (spec 016).</param>
/// <param name="IncludeEvCharger">The "count the charger with the house" checkbox (spec 016).</param>
public record LocationSummaryResponse(
    Guid Id,
    string Name,
    string Address,
    string Zone,
    string SerialNumber,
    bool HasNorgesPriceAgreement,
    bool IsActive,
    string Role,
    IReadOnlyList<MeterResponse> Meters,
    bool HasWaterHeater,
    bool HasEvCharger,
    bool IncludeEvCharger);
