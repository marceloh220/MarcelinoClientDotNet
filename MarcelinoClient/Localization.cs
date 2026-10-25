using System.Globalization;
using System.Windows;

namespace MarcelinoClient;

internal static class Localization
{
    private static readonly string[] SupportedLanguages = ["pt-BR", "en-US"];
    private static readonly object ResourceLock = new();
    private static IReadOnlyDictionary<string, string>? _strings;
    private static string _language = "pt-BR";
    public static IReadOnlyList<LanguageOption> Languages { get; } = SupportedLanguages
        .Select(name => new CultureInfo(name))
        .Select(culture => new LanguageOption(culture.Name, culture.NativeName))
        .ToArray();

    public static string NormalizeLanguage(string? language) =>
        SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase) ? language! : "pt-BR";

    public static void Apply(string? language)
    {
        var cultureName = NormalizeLanguage(language);
        _language = cultureName;
        var culture = CultureInfo.GetCultureInfo(cultureName);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        var application = Application.Current;
        var current = application.Resources.MergedDictionaries.FirstOrDefault(dictionary =>
            dictionary.Source?.OriginalString.Contains("/Localization/Strings.", StringComparison.OrdinalIgnoreCase) == true);
        if (current is not null)
        {
            application.Resources.MergedDictionaries.Remove(current);
        }

        var resources = new ResourceDictionary
        {
            Source = new Uri($"/MarcelinoClient;component/Localization/Strings.{cultureName}.xaml", UriKind.Relative)
        };
        application.Resources.MergedDictionaries.Add(resources);
        Volatile.Write(ref _strings, CopyStrings(resources));
    }

    public static string Get(string key, params object?[] arguments)
    {
        var strings = Volatile.Read(ref _strings) ?? GetFallbackStrings();
        var value = strings.TryGetValue(key, out var localized) ? localized : key;
        return arguments.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, arguments);
    }

    public static LocalizedText Message(string key, params object?[] arguments) => new(key, arguments);

    public static string Format(LocalizedText message) => Get(message.Key, message.Arguments
        .Select(argument => argument is LocalizedText nested ? nested.Format() : argument).ToArray());

    private static IReadOnlyDictionary<string, string> GetFallbackStrings()
    {
        lock (ResourceLock)
        {
            if (_strings is not null)
            {
                return _strings;
            }

            var resources = (ResourceDictionary)Application.LoadComponent(
                new Uri($"/MarcelinoClient;component/Localization/Strings.{_language}.xaml", UriKind.Relative));
            _strings = CopyStrings(resources);
            return _strings;
        }
    }

    private static IReadOnlyDictionary<string, string> CopyStrings(ResourceDictionary resources)
    {
        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in resources.Keys)
        {
            if (key is string name && resources[key] is string value)
            {
                strings[name] = value;
            }
        }

        return strings;
    }
}

internal sealed record LocalizedText(string Key, object?[] Arguments)
{
    internal string Format() => Localization.Format(this);
}

internal sealed record LanguageOption(string CultureName, string NativeName)
{
    public override string ToString() => NativeName;
}

