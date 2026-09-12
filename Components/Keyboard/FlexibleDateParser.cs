using System.Globalization;

namespace TexTrack.Web.Components.Keyboard;

public static class FlexibleDateParser
{
    private static readonly string[] FullDateFormats =
    [
        "dd-MMM-yyyy", "d-MMM-yyyy", "dd-MM-yyyy", "d-M-yyyy",
        "dd/MM/yyyy", "d/M/yyyy", "dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd"
    ];

    public static bool TryParse(string? text, DateOnly basis, out DateOnly parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var raw = text.Trim();
        if (DateOnly.TryParseExact(raw, FullDateFormats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out parsed))
            return true;

        var parts = raw.Split(['.', '/', '-', ' '],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is 2 or 3 && parts.All(x => int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            var day = int.Parse(parts[0], CultureInfo.InvariantCulture);
            var month = int.Parse(parts[1], CultureInfo.InvariantCulture);
            var year = parts.Length == 2 ? basis.Year : int.Parse(parts[2], CultureInfo.InvariantCulture);
            if (parts.Length == 3 && parts[2].Length == 2) year += 2000;
            return TryCreate(day, month, year, out parsed);
        }

        if (!raw.All(char.IsDigit)) return false;
        return raw.Length switch
        {
            <= 2 => TryCreate(int.Parse(raw, CultureInfo.InvariantCulture), basis.Month, basis.Year, out parsed),
            3 or 4 => TryCreate(
                int.Parse(raw[..^2], CultureInfo.InvariantCulture),
                int.Parse(raw[^2..], CultureInfo.InvariantCulture), basis.Year, out parsed),
            6 => TryCreate(int.Parse(raw[..2], CultureInfo.InvariantCulture),
                int.Parse(raw.Substring(2, 2), CultureInfo.InvariantCulture),
                2000 + int.Parse(raw[4..], CultureInfo.InvariantCulture), out parsed),
            8 => TryCreate(int.Parse(raw[..2], CultureInfo.InvariantCulture),
                int.Parse(raw.Substring(2, 2), CultureInfo.InvariantCulture),
                int.Parse(raw[4..], CultureInfo.InvariantCulture), out parsed),
            _ => false
        };
    }

    private static bool TryCreate(int day, int month, int year, out DateOnly parsed)
    {
        parsed = default;
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
        parsed = new DateOnly(year, month, day);
        return true;
    }
}
