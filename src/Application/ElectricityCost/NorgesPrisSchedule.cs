using Domain.Common.Entities;

namespace Application.ElectricityCost;

/// <summary>Which Norgespris rate applies to an hour. Norgespris changes at local midnight, so Oslo dates decide.</summary>
public static class NorgesPrisSchedule
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    /// <summary>The Oslo calendar date of a UTC instant.</summary>
    public static DateOnly OsloDateOf(DateTime utcInstant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utcInstant, DateTimeKind.Utc), Oslo));

    /// <summary>
    /// The rate with the latest start date on or before the Oslo date of the hour, or null when the hour is before
    /// the earliest rate. <paramref name="ratesOrderedByValidFrom"/> must be ordered by start date, oldest first.
    /// </summary>
    public static NorgesPrisRate? RateFor(IReadOnlyList<NorgesPrisRate> ratesOrderedByValidFrom, DateTime hourStartUtc)
    {
        DateOnly date = OsloDateOf(hourStartUtc);
        NorgesPrisRate? found = null;
        foreach (NorgesPrisRate rate in ratesOrderedByValidFrom)
        {
            if (rate.ValidFrom > date)
            {
                break;
            }

            found = rate;
        }

        return found;
    }
}
