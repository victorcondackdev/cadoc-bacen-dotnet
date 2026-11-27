using System.Globalization;

namespace CadocBacen.Internal;

/// <summary>A set of non-business dates.</summary>
public interface IHolidays
{
    /// <summary>Whether the date is a holiday.</summary>
    bool IsHoliday(DateOnly date);
}

/// <summary>
/// The Brazilian national banking calendar: national holidays (including Black
/// Consciousness Day, national since 2024) plus Carnival Monday and Tuesday.
/// </summary>
public sealed class NationalHolidays : IHolidays
{
    private static readonly Dictionary<(int Month, int Day), string> Fixed = new()
    {
        [(1, 1)] = "Confraternização Universal",
        [(4, 21)] = "Tiradentes",
        [(5, 1)] = "Dia do Trabalho",
        [(9, 7)] = "Independência",
        [(10, 12)] = "Nossa Senhora Aparecida",
        [(11, 2)] = "Finados",
        [(11, 15)] = "Proclamação da República",
        [(12, 25)] = "Natal",
    };

    /// <summary>Creates the calendar with optional local holidays.</summary>
    public NationalHolidays(IReadOnlyDictionary<DateOnly, string>? extra = null)
    {
        Extra = extra ?? new Dictionary<DateOnly, string>();
    }

    /// <summary>Extra (local) holidays.</summary>
    public IReadOnlyDictionary<DateOnly, string> Extra { get; }

    /// <summary>The holiday name, or empty.</summary>
    public string Name(DateOnly d)
    {
        if (Extra.TryGetValue(d, out string? s))
        {
            return s;
        }

        if (Fixed.TryGetValue((d.Month, d.Day), out s))
        {
            return s;
        }

        if (d.Month == 11 && d.Day == 20 && d.Year >= 2024)
        {
            return "Dia Nacional de Zumbi e da Consciência Negra";
        }

        DateOnly e = BankDays.Easter(d.Year);
        if (d == e.AddDays(-48))
        {
            return "Carnaval (segunda-feira)";
        }

        if (d == e.AddDays(-47))
        {
            return "Carnaval (terça-feira)";
        }

        if (d == e.AddDays(-2))
        {
            return "Sexta-feira Santa";
        }

        return d == e.AddDays(60) ? "Corpus Christi" : "";
    }

    /// <inheritdoc />
    public bool IsHoliday(DateOnly date) => Name(date).Length > 0;
}

/// <summary>Business days on the national banking calendar.</summary>
public static class BankDays
{
    /// <summary>Brasília time (no daylight saving since 2019).</summary>
    public static readonly TimeSpan Brt = TimeSpan.FromHours(-3);

    /// <summary>Parses YYYY-MM-DD.</summary>
    public static DateOnly ParseDate(string s) => DateOnly.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Formats YYYY-MM-DD.</summary>
    public static string Format(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>The instant at hh:mm of a date in Brasília time.</summary>
    public static DateTimeOffset At(this DateOnly d, int hour, int minute) => new(d.Year, d.Month, d.Day, hour, minute, 0, Brt);

    /// <summary>The civil date of an instant in Brasília time.</summary>
    public static DateOnly DateOf(DateTimeOffset t) => DateOnly.FromDateTime(t.ToOffset(Brt).DateTime);

    /// <summary>Days from a to b.</summary>
    public static int DaysUntil(this DateOnly a, DateOnly b) => b.DayNumber - a.DayNumber;

    /// <summary>Easter Sunday (Gregorian, anonymous algorithm).</summary>
    public static DateOnly Easter(int year)
    {
        int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4;
        int f = (b + 8) / 25, g = (b - f + 1) / 3, h = ((19 * a) + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4, l = (32 + (2 * e) + (2 * i) - h - k) % 7, m = (a + (11 * h) + (22 * l)) / 451;
        int month = (h + l - (7 * m) + 114) / 31;
        int day = ((h + l - (7 * m) + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    /// <summary>Weekday and not a holiday.</summary>
    public static bool IsBusinessDay(IHolidays? h, DateOnly d)
        => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && (h is null || !h.IsHoliday(d));

    /// <summary>d if a business day, else the next one.</summary>
    public static DateOnly NextBusinessDay(IHolidays? h, DateOnly d)
    {
        while (!IsBusinessDay(h, d))
        {
            d = d.AddDays(1);
        }

        return d;
    }

    /// <summary>d if a business day, else the previous one.</summary>
    public static DateOnly PreviousBusinessDay(IHolidays? h, DateOnly d)
    {
        while (!IsBusinessDay(h, d))
        {
            d = d.AddDays(-1);
        }

        return d;
    }
}
