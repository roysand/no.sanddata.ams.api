namespace Application.ElectricityCost;

/// <summary>
/// The Norwegian public holidays that count as weekend for the grid tariff, computed from the calendar (the Easter
/// based ones from Easter Sunday), so no yearly list has to be kept up to date. Easter Sunday and Whit Sunday are
/// Sundays and need no entry.
/// </summary>
public static class NorwegianHolidays
{
    public static bool IsHoliday(DateOnly date)
    {
        if (IsFixedHoliday(date))
        {
            return true;
        }

        DateOnly easter = EasterSunday(date.Year);
        int daysFromEaster = date.DayNumber - easter.DayNumber;

        // Maundy Thursday, Good Friday, Easter Monday, Ascension Day, Whit Monday.
        return daysFromEaster is -3 or -2 or 1 or 39 or 50;
    }

    private static bool IsFixedHoliday(DateOnly date) =>
        (date.Month, date.Day) is (1, 1) or (5, 1) or (5, 17) or (12, 25) or (12, 26);

    /// <summary>Easter Sunday of the Gregorian calendar (the anonymous Gregorian algorithm).</summary>
    public static DateOnly EasterSunday(int year)
    {
        int a = year % 19;
        int b = year / 100;
        int c = year % 100;
        int d = b / 4;
        int e = b % 4;
        int f = (b + 8) / 25;
        int g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4;
        int k = c % 4;
        int l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31;
        int day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(year, month, day);
    }
}
