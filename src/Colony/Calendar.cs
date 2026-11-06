// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Globalization;

namespace WardensReckoning.Colony;

/// <summary>
/// The calendar (the design library's Calendar and Seasonal Timekeeping): 12 months of 28 days, so 336 days a year
/// and exactly 48 weeks. Every month, and so every year and every season, begins on a Monday. Spring is months 1
/// to 3, summer 4 to 6, autumn 7 to 9, winter 10 to 12.
///
/// Time itself is one whole number: the tick. One tick is one in-game minute (library Review 29), counted from
/// midnight at the start of Year 1, Day 1. Every date is worked out from it here, in one place, so no system ever
/// counts months or seasons by itself.
/// </summary>
public static class Calendar
{
    public const int MinutesPerHour = 60;
    public const int HoursPerDay = 24;
    /// <summary>1,440: one tick is one minute.</summary>
    public const int TicksPerDay = MinutesPerHour * HoursPerDay;
    public const int DaysPerWeek = 7;
    public const int DaysPerMonth = 28;
    public const int MonthsPerYear = 12;
    public const int MonthsPerSeason = 3;
    /// <summary>336.</summary>
    public const int DaysPerYear = DaysPerMonth * MonthsPerYear;
    /// <summary>483,840.</summary>
    public const long TicksPerYear = (long)TicksPerDay * DaysPerYear;

    /// <summary>Month names (library §10.2, "proposed"): presentation only. Saves keep month numbers, never
    /// names, so these can change without breaking a save.</summary>
    public static readonly string[] MonthNames =
        { "Aven", "Saeren", "Belwyn", "Solren", "Caeren", "Harven", "Orven", "Rusken", "Derven", "Kelden", "Morren", "Hearthren" };

    /// <summary>Each month's common name, for tooltips and the calendar view.</summary>
    public static readonly string[] CommonMonthNames =
    {
        "Thaw Month", "Sowing Month", "Bloom Month", "Sun Month", "High Summer", "Hay Month",
        "Harvest Month", "Redleaf Month", "Store Month", "First Frost", "Deep Winter", "Hearth Month",
    };

    public static readonly string[] WeekdayNames = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
    public static readonly string[] SeasonNames = { "Spring", "Summer", "Autumn", "Winter" };

    /// <summary>
    /// The tick at a date and time (library §7.2): `year` 1 or more, `month` 1 to 12, `day` 1 to 28, `hour` 0 to 23,
    /// `minute` 0 to 59. Anything else is refused, so a wrong date can never slip into the simulation.
    /// </summary>
    public static long TickOf(int year, int month, int day, int hour = 0, int minute = 0)
    {
        Check(year >= 1, "the year", year, "1 or more");
        Check(month is >= 1 and <= MonthsPerYear, "the month", month, "1 to 12");
        Check(day is >= 1 and <= DaysPerMonth, "the day", day, "1 to 28");
        Check(hour is >= 0 and < HoursPerDay, "the hour", hour, "0 to 23");
        Check(minute is >= 0 and < MinutesPerHour, "the minute", minute, "0 to 59");
        long absoluteDay = (long)(year - 1) * DaysPerYear + (month - 1) * DaysPerMonth + (day - 1);
        return absoluteDay * TicksPerDay + hour * MinutesPerHour + minute;
    }

    static void Check(bool ok, string what, int value, string allowed)
    {
        if (!ok)
            throw new ArgumentOutOfRangeException(what, value, what + " must be " + allowed + ", not " + value);
    }
}

/// <summary>
/// One moment of colony time: a tick, and everything the calendar says about it. A `readonly record struct` is a
/// small value (like a Python frozen dataclass): two moments with the same tick are equal, and nothing can change
/// one after it is made.
/// </summary>
public readonly record struct Moment(long Tick)
{
    /// <summary>Days since Year 1, Day 1 (the library's AbsoluteDay).</summary>
    public long AbsoluteDay => Tick / Calendar.TicksPerDay;

    /// <summary>The minute of the day, 0 (midnight) to 1,439.</summary>
    public int MinuteOfDay => (int)(Tick % Calendar.TicksPerDay);

    public int Year => (int)(AbsoluteDay / Calendar.DaysPerYear) + 1;
    /// <summary>1 to 336.</summary>
    public int DayOfYear => (int)(AbsoluteDay % Calendar.DaysPerYear) + 1;
    /// <summary>1 to 12.</summary>
    public int Month => (DayOfYear - 1) / Calendar.DaysPerMonth + 1;
    /// <summary>1 to 28.</summary>
    public int DayOfMonth => (DayOfYear - 1) % Calendar.DaysPerMonth + 1;
    /// <summary>0 (Monday) to 6 (Sunday): 336 is a whole number of weeks, so every year starts on Monday.</summary>
    public int Weekday => (int)(AbsoluteDay % Calendar.DaysPerWeek);
    /// <summary>0 (spring) to 3 (winter).</summary>
    public int Season => (Month - 1) / Calendar.MonthsPerSeason;
    public int Hour => MinuteOfDay / Calendar.MinutesPerHour;
    public int Minute => MinuteOfDay % Calendar.MinutesPerHour;

    /// <summary>The moment `ticks` later.</summary>
    public Moment After(long ticks) => new(Tick + ticks);

    /// <summary>As the library shows a date: "14th of Orven, Year 12, 06:30".</summary>
    public override string ToString() =>
        Ordinal(DayOfMonth) + " of " + Calendar.MonthNames[Month - 1] + ", Year " + Year.ToString(CultureInfo.InvariantCulture)
        + ", " + Hour.ToString("00", CultureInfo.InvariantCulture) + ":" + Minute.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>1st, 2nd, 3rd, 4th... 11th, 12th, 13th, 21st, 22nd.</summary>
    public static string Ordinal(int n)
    {
        string end = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n.ToString(CultureInfo.InvariantCulture) + end;
    }
}
