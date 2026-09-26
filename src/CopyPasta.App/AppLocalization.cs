using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using CopyPasta.Core.Localization;
using CopyPasta.Core.Menu;

namespace CopyPasta.App;

/// <summary>
/// Loads the string tables extracted from Clipy's Apple string catalogues.
/// </summary>
/// <remarks>
/// <para>
/// The translations are the one asset worth carrying over wholesale: they are MIT-licensed code
/// resources, unlike the icons (see PORTING_PLAN.md §2), and they cover de, it, ja, pt-BR, uk and
/// zh-Hans. <c>tools/ExtractStrings.ps1</c> regenerates them from a newer macOS source tree.
/// </para>
/// <para>
/// Embedded rather than loose files so the app is a single self-contained executable, and so a
/// missing locale file is a build-time fact rather than a runtime surprise.
/// </para>
/// </remarks>
public static class AppLocalization
{
    private const string ResourcePrefix = "CopyPasta.App.Resources.Strings.strings.";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    /// <summary>Builds a localizer for a culture, defaulting to the current UI culture.</summary>
    public static Localizer Load(CultureInfo? culture = null) =>
        LocaleResolver.Build(culture ?? CultureInfo.CurrentUICulture, LoadTable);

    /// <summary>Locales this build actually ships, for diagnostics.</summary>
    public static IReadOnlyList<string> AvailableLocales =>
        typeof(AppLocalization).Assembly
            .GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(name => name[ResourcePrefix.Length..^".json".Length])
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Menu text in the user's language.</summary>
    public static MenuStrings MenuStrings(Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);

        // The keys are the English strings the macOS catalogues use, so an untranslated entry
        // comes back as readable English rather than a token.
        return new MenuStrings
        {
            History = localizer["History"],
            Snippets = localizer["Snippet"],
            ClearHistory = localizer["Clear History"],
            EditSnippets = localizer["Edit Snippets"],
            Settings = localizer["Settings…"],

            // The catalogues label this button "Check Now…" inside a Software Update pane, where
            // the surrounding heading supplies the subject. Standing alone in a tray menu it needs
            // to say what is being checked, so English uses the longer phrase and the translated
            // languages get the catalogue's wording rather than untranslated English.
            CheckForUpdates = localizer.HasTranslation("Check Now…")
                ? localizer["Check Now…"]
                : "Check for Updates…",
            // The catalogues have no English row for this one, so an untranslated lookup returns
            // the key itself. English gets the bare verb; every other locale names the app.
            Quit = localizer["Quit CopyPasta"] is "Quit CopyPasta" ? "Quit" : localizer["Quit CopyPasta"],
        };
    }

    private static IReadOnlyDictionary<string, string>? LoadTable(string locale)
    {
        Assembly assembly = typeof(AppLocalization).Assembly;

        using Stream? stream = assembly.GetManifestResourceStream($"{ResourcePrefix}{locale}.json");
        if (stream is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(stream, Options);
        }
        catch (JsonException)
        {
            // A malformed table must not stop the app starting; English is always available.
            return null;
        }
    }
}
