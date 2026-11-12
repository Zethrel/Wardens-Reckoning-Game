// Copyright (c) 2026 Zethrel. All rights reserved. See LICENSE.
using System.Globalization;

namespace WardensReckoning.Colony;

/// <summary>
/// The calendar (the design library's Calendar and Seasonal Timekeeping): 12 months of 28 days, so 336 days a year
/// and exactly 48 weeks. Every month, and so every year and every season, begins on a Monday. Spring is months 1
/// to 3, summer 4 to 6, autumn 7 to 9, winter 10 to 12.
///
/// Time itself is one whole number: the tick. One tick is one in-game second (library Review 39; until step 21 it
/// was a minute, Review 29), counted from midnight at the start of Year 1, Day 1. At ×1 a real second runs 24 of
/// them (Review 41: `TicksPerRealSecond`), so a day lasts an hour. Every date is worked out from it here, in one place, so no system ever
/// counts months or seasons by itself.
/// </summary>
public static class Calendar
{
    public const int SecondsPerMinute = 60;
    public const int MinutesPerHour = 60;
    public const int HoursPerDay = 24;
    /// <summary>1,440.</summary>
    public const int MinutesPerDay = MinutesPerHour * HoursPerDay;
    /// <summary>60: one tick is one second.</summary>
    public const int TicksPerMinute = SecondsPerMinute;
    /// <summary>3,600.</summary>
    public const int TicksPerHour = TicksPerMinute * MinutesPerHour;
    /// <summary>86,400.</summary>
    public const int TicksPerDay = TicksPerHour * HoursPerDay;
    public const int DaysPerWeek = 7;
    public const int DaysPerMonth = 28;
    public const int MonthsPerYear = 12;

    /// <summary>
    /// Ticks a real second at ×1 (library Review 41, the hybrid clock): 24, so a day of 86,400 ticks lasts 3,600 real
    /// seconds, an hour. The one constant for both things that need it: the pacer (how many ticks to run) and the
    /// content's physical values (walking, loading, deciding), written in real seconds at ×1 and turned into ticks
    /// once, when the content is read (`TicksOf`, and rates divided by it).
    /// </summary>
    public const int TicksPerRealSecond = 24;

    /// <summary>
    /// A duration written in real seconds at ×1, in ticks (library Review 41): `seconds × 24`, rounded to the nearest
    /// whole tick with halves away from zero, and at least 1 tick when it is above 0 (0 stays 0). So 5 seconds are
    /// 120 ticks, 0.5 are 12, and 0.01 is 1 rather than nothing.
    /// </summary>
    public static int TicksOf(double realSeconds)
    {
        double ticks = Math.Round(realSeconds * TicksPerRealSecond, MidpointRounding.AwayFromZero);
        return realSeconds > 0 ? Math.Max(1, (int)ticks) : 0;
    }
    public const int MonthsPerSeason = 3;
    /// <summary>336.</summary>
    public const int DaysPerYear = DaysPerMonth * MonthsPerYear;
    /// <summary>29,030,400.</summary>
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
    /// `minute` and `second` 0 to 59. Anything else is refused, so a wrong date can never slip into the simulation.
    /// </summary>
    public static long TickOf(int year, int month, int day, int hour = 0, int minute = 0, int second = 0)
    {
        Check(year >= 1, "the year", year, "1 or more");
        Check(month is >= 1 and <= MonthsPerYear, "the month", month, "1 to 12");
        Check(day is >= 1 and <= DaysPerMonth, "the day", day, "1 to 28");
        Check(hour is >= 0 and < HoursPerDay, "the hour", hour, "0 to 23");
        Check(minute is >= 0 and < MinutesPerHour, "the minute", minute, "0 to 59");
        Check(second is >= 0 and < SecondsPerMinute, "the second", second, "0 to 59");
        long absoluteDay = (long)(year - 1) * DaysPerYear + (month - 1) * DaysPerMonth + (day - 1);
        return absoluteDay * TicksPerDay + hour * TicksPerHour + minute * TicksPerMinute + second;
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

    /// <summary>The second of the day, 0 (midnight) to 86,399.</summary>
    public int SecondOfDay => (int)(Tick % Calendar.TicksPerDay);

    /// <summary>The minute of the day, 0 (midnight) to 1,439.</summary>
    public int MinuteOfDay => SecondOfDay / Calendar.TicksPerMinute;

    /// <summary>Whole minutes since midnight at the start of Year 1, Day 1.</summary>
    public long WholeMinutes => Tick / Calendar.TicksPerMinute;

    /// <summary>Whether this moment is a whole minute (second 0): when everything but walking is worked out.</summary>
    public bool OnTheMinute => Tick % Calendar.TicksPerMinute == 0;

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
    public int Second => SecondOfDay % Calendar.SecondsPerMinute;

    /// <summary>The moment `ticks` (seconds) later.</summary>
    public Moment After(long ticks) => new(Tick + ticks);

    /// <summary>The time of day with its seconds: "06:30:15".</summary>
    public string Clock => Hour.ToString("00", CultureInfo.InvariantCulture) + ":" + Minute.ToString("00", CultureInfo.InvariantCulture)
                           + ":" + Second.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>As the library shows a date: "14th of Orven, Year 12, 06:30".</summary>
    public override string ToString() =>
        Ordinal(DayOfMonth) + " of " + Calendar.MonthNames[Month - 1] + ", Year " + Year.ToString(CultureInfo.InvariantCulture)
        + ", " + Hour.ToString("00", CultureInfo.InvariantCulture) + ":" + Minute.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>As the colony screen's bar shows it, with the seconds: "14th of Orven, Year 12, 06:30:15".</summary>
    public string WithSeconds =>
        Ordinal(DayOfMonth) + " of " + Calendar.MonthNames[Month - 1] + ", Year " + Year.ToString(CultureInfo.InvariantCulture) + ", " + Clock;

    /// <summary>1st, 2nd, 3rd, 4th... 11th, 12th, 13th, 21st, 22nd.</summary>
    public static string Ordinal(int n)
    {
        string end = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n.ToString(CultureInfo.InvariantCulture) + end;
    }
}
