using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace UserIdentityDesktop.Converters;

/// <summary>Calendar days stay local dates; selecting a day never converts it through UTC.</summary>
public sealed class LocalCalendarDateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DateTimeOffset date => date.Date,
        DateTime date => date.Date,
        _ => null
    };
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime date) return null;
        var day = DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);
        return new DateTimeOffset(day, TimeZoneInfo.Local.GetUtcOffset(day));
    }
}

public sealed class CalendarDisplayDateConverter : IValueConverter
{
    public string Format { get; set; } = "ddd, d MMM yyyy";
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is DateTime date ? date.ToString(Format,culture) : "";
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(value as string)) return null;
        var text = (string)value;
        if (DateTime.TryParseExact(text,Format,culture,DateTimeStyles.None,out var date) ||
            DateTime.TryParseExact(text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date) ||
            DateTime.TryParse(text,culture,DateTimeStyles.AllowWhiteSpaces,out date)) return date.Date;
        throw new FormatException("Choose a day from the calendar or enter a valid date.");
    }
}
