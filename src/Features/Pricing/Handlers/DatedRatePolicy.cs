using Domain.Common;

namespace Features.Pricing.Handlers;

/// <summary>
/// The rules for adding and deleting a dated value, the same as for the Norgespris rates. Used by the national rate
/// and the grid tariff rate handlers of this slice (and nowhere else).
/// </summary>
public static class DatedRatePolicy
{
    /// <summary>
    /// Null when a row may start on <paramref name="validFrom"/>. The checks run in this order: the date is already
    /// used; a date of today or earlier that is not later than every existing one; a date of today or earlier without
    /// the confirmation that past costs are recalculated.
    /// </summary>
    /// <param name="existingStarts">The start dates of the rows of the same kind or tariff.</param>
    /// <param name="today">Today in Norway.</param>
    public static Error? Check(
        IReadOnlyCollection<DateOnly> existingStarts, DateOnly validFrom, bool confirmPastDate, DateOnly today)
    {
        if (existingStarts.Contains(validFrom))
        {
            return DateExists;
        }

        if (validFrom > today)
        {
            return null;
        }

        // A past date may only correct the newest row: an older one would silently rewrite history already shown.
        if (existingStarts.Count > 0 && validFrom < existingStarts.Max())
        {
            return Error.Conflict(
                "Pricing.NotNewest", "A rate that starts today or earlier must start after every existing rate");
        }

        if (!confirmPastDate)
        {
            return Error.Conflict(
                "Pricing.ConfirmationRequired",
                "This rate starts today or earlier, so costs shown for past hours will be recalculated. " +
                "Confirm to add it");
        }

        return null;
    }

    /// <summary>A row that has started (today included) has priced hours already and cannot be deleted.</summary>
    public static bool HasStarted(DateOnly validFrom, DateOnly today) => validFrom <= today;

    public static Error DateExists { get; } =
        Error.Conflict("Pricing.DateExists", "Another rate already starts on this date");

    public static Error AlreadyStarted { get; } =
        Error.Conflict("Pricing.AlreadyStarted", "A rate that has already started cannot be deleted");

    public static Error NotFound { get; } = Error.NotFound("Pricing.NotFound", "Rate not found");
}
