using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using CopyPasta.Core.Localization;
using CopyPasta.Core.Menu;

namespace CopyPasta.App.Tests;

/// <summary>
/// The translations carried over from Clipy's string catalogues.
/// </summary>
public class AppLocalizationTests
{
    [Fact]
    public void Every_language_Clipy_shipped_is_embedded()
    {
        // Clipy ships these six translations plus English. They are MIT-licensed code resources,
        // so unlike the icons they can come over wholesale — losing one is a regression.
        Assert.Equal(
            ["de", "en", "it", "ja", "pt-BR", "uk", "zh-Hans"],
            AppLocalization.AvailableLocales);
    }

    [Theory]
    [InlineData("de", "Verlauf")]
    [InlineData("ja", "履歴")]
    [InlineData("zh-Hans", "历史")]
    public void A_known_string_is_translated(string locale, string expected)
    {
        Localizer localizer = AppLocalization.Load(new CultureInfo(locale));

        Assert.Equal(expected, localizer["History"]);
    }

    [Fact]
    public void Each_language_carries_a_substantial_table()
    {
        foreach (string locale in AppLocalization.AvailableLocales.Where(name => name != "en"))
        {
            Localizer localizer = AppLocalization.Load(new CultureInfo(locale));

            Assert.True(
                localizer.Count > 100,
                $"{locale} has only {localizer.Count} strings, which suggests a broken extraction");
        }
    }

    [Fact]
    public void An_unsupported_language_falls_back_to_English()
    {
        Localizer localizer = AppLocalization.Load(new CultureInfo("fi"));

        // Not a token: the key is the English source, so the fallback is readable.
        Assert.Equal("History", localizer["History"]);
    }

    [Fact]
    public void A_regional_variant_resolves_to_its_language()
    {
        Assert.Equal(
            AppLocalization.Load(new CultureInfo("de"))["History"],
            AppLocalization.Load(new CultureInfo("de-AT"))["History"]);
    }

    [Fact]
    public void An_untranslated_key_comes_back_as_itself()
    {
        Localizer localizer = AppLocalization.Load(new CultureInfo("de"));

        Assert.Equal("Not A Real String", localizer["Not A Real String"]);
    }

    // ---- Menu text --------------------------------------------------------------------

    [Fact]
    public void Menu_captions_are_translated()
    {
        MenuStrings strings = AppLocalization.MenuStrings(
            AppLocalization.Load(new CultureInfo("de")));

        Assert.Equal("Verlauf", strings.History);
        Assert.NotEqual("Clear History", strings.ClearHistory);
    }

    [Fact]
    public void Menu_captions_in_English_read_as_English()
    {
        MenuStrings strings = AppLocalization.MenuStrings(
            AppLocalization.Load(new CultureInfo("en")));

        Assert.Equal("History", strings.History);
        Assert.Equal("Clear History", strings.ClearHistory);
    }

    [Fact]
    public void The_quit_caption_does_not_carry_the_macOS_product_name()
    {
        // macOS keys this string as "Quit Clipy". This port must not ship a menu item naming a
        // product it is not (PORTING_PLAN.md §2), so English falls back to a plain "Quit".
        MenuStrings strings = AppLocalization.MenuStrings(
            AppLocalization.Load(new CultureInfo("en")));

        Assert.Equal("Quit", strings.Quit);
    }

    [Fact]
    public void No_menu_caption_is_empty_in_any_shipped_language()
    {
        foreach (string locale in AppLocalization.AvailableLocales)
        {
            MenuStrings strings = AppLocalization.MenuStrings(
                AppLocalization.Load(new CultureInfo(locale)));

            Assert.All(
                new[]
                {
                    strings.History, strings.Snippets, strings.ClearHistory,
                    strings.EditSnippets, strings.Settings, strings.Quit,
                },
                caption => Assert.False(string.IsNullOrWhiteSpace(caption), $"empty caption in {locale}"));
        }
    }

    /// <summary>Reads the JSON tables exactly as they are embedded in the shipping assembly.</summary>
    private static Dictionary<string, string> EmbeddedTable(string locale)
    {
        Assembly assembly = typeof(AppLocalization).Assembly;
        string name = $"CopyPasta.App.Resources.Strings.strings.{locale}.json";

        using Stream stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"no embedded table for {locale}");

        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"unreadable table for {locale}");
    }

    [Fact]
    public void No_shipped_string_names_the_project_this_was_ported_from()
    {
        // The catalogues come from the macOS app, so its name is all through them: the tray menu
        // read "Clipy beenden" in German while English was clean, because English has no row for
        // that key and falls back to the plain verb. tools/ExtractStrings.ps1 rewrites the name on
        // the way through; this is what stops a hand-edit or a re-extract putting it back.
        //
        // Keys are checked as well as values — the lookup key is what the code asks for, and a
        // renamed value under an old key would silently stop resolving.
        foreach (string locale in AppLocalization.AvailableLocales)
        {
            foreach ((string key, string value) in EmbeddedTable(locale))
            {
                Assert.DoesNotContain("Clipy", key, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("ClipMenu", key, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Clipy", value, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("ClipMenu", value, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Theory]
    [InlineData("de", "CopyPasta beenden")]
    [InlineData("it", "Esci da CopyPasta")]
    [InlineData("pt-BR", "Encerrar CopyPasta")]
    public void The_quit_caption_names_this_app_in_translated_languages(string locale, string expected)
    {
        // The one Clipy-named key the menu actually looks up, so it is the one that reached users.
        MenuStrings strings = AppLocalization.MenuStrings(
            AppLocalization.Load(new CultureInfo(locale)));

        Assert.Equal(expected, strings.Quit);
    }

    [Fact]
    public void Strings_for_macOS_only_features_are_not_shipped()
    {
        // Windows needs no Accessibility grant to send a paste, and there is no donation panel or
        // usage logging, so these would be untriggerable text carrying a stale name.
        string[] dropped =
        [
            "Please allow Accessibility",
            "Open System Settings",
            "Donation message",
        ];

        foreach (string locale in AppLocalization.AvailableLocales)
        {
            Dictionary<string, string> table = EmbeddedTable(locale);
            Assert.All(dropped, key => Assert.DoesNotContain(key, table.Keys));
        }
    }
}
