using System.Globalization;

namespace CopyPasta.Core.Localization;

/// <summary>
/// Looks up display strings by their English source text.
/// </summary>
/// <remarks>
/// <para>
/// The key <em>is</em> the English string, matching the Apple string catalogues the translations
/// come from. That has a useful property: a missing translation falls back to readable English
/// rather than to a placeholder token like <c>settings_general_title</c>, so a half-translated
/// build is still usable rather than broken.
/// </para>
/// <para>
/// Immutable once built. Changing language means building a new one, which is also when the UI has
/// to be rebuilt anyway.
/// </para>
/// </remarks>
public sealed class Localizer
{
    private readonly IReadOnlyDictionary<string, string> _table;

    public Localizer(IReadOnlyDictionary<string, string> table, string localeName = "en")
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentException.ThrowIfNullOrWhiteSpace(localeName);

        _table = table;
        LocaleName = localeName;
    }

    /// <summary>An untranslated localizer: every key resolves to itself.</summary>
    public static Localizer Invariant { get; } = new(new Dictionary<string, string>());

    /// <summary>The locale this was built for, for diagnostics.</summary>
    public string LocaleName { get; }

    /// <summary>Number of translated entries.</summary>
    public int Count => _table.Count;

    public string this[string key] => Get(key);

    /// <summary>The translation for a key, or the key itself when there is none.</summary>
    public string Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return _table.TryGetValue(key, out string? value) && value.Length > 0 ? value : key;
    }

    /// <summary>True when this key has a real translation rather than the English fallback.</summary>
    public bool HasTranslation(string key) =>
        key is not null && _table.TryGetValue(key, out string? value) && value.Length > 0;
}

/// <summary>Works out which locale files to load, and in what order.</summary>
public static class LocaleResolver
{
    /// <summary>The base locale, always loaded first so its entries can be overridden.</summary>
    public const string FallbackLocale = "en";

    /// <summary>
    /// Locale names to load for a culture, least specific first.
    /// </summary>
    /// <remarks>
    /// Least-specific-first so later tables override earlier ones: a Brazilian user gets the
    /// English base, then anything generic Portuguese, then the pt-BR specifics on top. The macOS
    /// catalogues only ship <c>pt-BR</c>, but resolving the chain properly means adding plain
    /// <c>pt</c> later needs no code change.
    /// </remarks>
    public static IReadOnlyList<string> CandidateLocales(CultureInfo? culture)
    {
        culture ??= CultureInfo.InvariantCulture;

        List<string> candidates = [FallbackLocale];

        // Walk from the neutral culture down to the specific one.
        List<string> chain = [];
        for (CultureInfo current = culture;
             !string.IsNullOrEmpty(current.Name);
             current = current.Parent)
        {
            chain.Add(current.Name);

            // Parent of a neutral culture is the invariant culture, whose Name is empty.
            if (ReferenceEquals(current, current.Parent))
            {
                break;
            }
        }

        chain.Reverse();

        foreach (string name in chain)
        {
            if (!candidates.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(name);
            }
        }

        return candidates;
    }

    /// <summary>
    /// Builds a localizer by merging every table a loader can supply for the culture.
    /// </summary>
    /// <param name="load">
    /// Returns the entries for one locale name, or null when that locale is not shipped.
    /// </param>
    public static Localizer Build(
        CultureInfo? culture,
        Func<string, IReadOnlyDictionary<string, string>?> load)
    {
        ArgumentNullException.ThrowIfNull(load);

        Dictionary<string, string> merged = [];
        string resolved = FallbackLocale;

        foreach (string locale in CandidateLocales(culture))
        {
            IReadOnlyDictionary<string, string>? table = load(locale);
            if (table is null)
            {
                continue;
            }

            foreach ((string key, string value) in table)
            {
                merged[key] = value;
            }

            resolved = locale;
        }

        return new Localizer(merged, resolved);
    }
}
