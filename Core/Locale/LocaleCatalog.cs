using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.Json;

namespace UserIdentityDesktop.Core.Locale;

/// <summary>Allowlisted, plain-text product messages. Protocol values and user content are never translated here.</summary>
public static class LocaleCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> catalogs =
        new Dictionary<string, IReadOnlyDictionary<string, string>> { ["en"] = Read("en"), ["ar"] = Read("ar"), ["el"] = Read("el") };
    public static bool IsSupported(string? locale) => locale is "en" or "ar" or "el";
    public static string Current { get; private set; } = "en";
    public static event Action? Changed;
    public static CultureInfo DisplayCulture => CultureInfo.GetCultureInfo(Current switch { "ar" => "ar-EG", "el" => "el-GR", _ => "en-US" });
    public static void Set(string locale)
    {
        if (!IsSupported(locale)) throw new ArgumentException("Choose English, Arabic or Greek.", nameof(locale));
        if (Current == locale) return;
        Current = locale; Changed?.Invoke();
    }
    public static string Text(string key, string? locale = null)
    {
        var selected = IsSupported(locale) ? locale! : Current;
        return catalogs[selected].GetValueOrDefault(key) ?? catalogs["en"].GetValueOrDefault(key) ?? key;
    }
    public static string Format(FormattableString message, string? locale = null)
    {
        var selected = IsSupported(locale) ? locale! : Current;
        var culture = CultureInfo.GetCultureInfo(selected switch { "ar" => "ar-EG", "el" => "el-GR", _ => "en-US" });
        return string.Format(culture, Text(message.Format, selected), message.GetArguments());
    }
    private static IReadOnlyDictionary<string, string> Read(string locale)
    {
        using var stream = typeof(LocaleCatalog).Assembly.GetManifestResourceStream($"UserIdentityDesktop.Core.Locale.{locale}.json")
            ?? throw new InvalidOperationException("A language catalog is missing.");
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.GetProperty("messages").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.Ordinal);
    }
}
