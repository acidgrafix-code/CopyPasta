using System.Globalization;
using CopyPasta.Core.Localization;

namespace CopyPasta.Core.Tests;

public class LocalizerTests
{
    private static Localizer With(params (string Key, string Value)[] entries) =>
        new(entries.ToDictionary(entry => entry.Key, entry => entry.Value), "de");

    // ---- Lookup -----------------------------------------------------------------------

    [Fact]
    public void A_translated_key_returns_its_translation()
    {
        Localizer localizer = With(("History", "Verlauf"));

        Assert.Equal("Verlauf", localizer.Get("History"));
        Assert.Equal("Verlauf", localizer["History"]);
    }

    [Fact]
    public void An_untranslated_key_falls_back_to_readable_English()
    {
        // The whole point of keying on the English source: a half-translated build still reads,
        // rather than showing tokens like settings_general_title.
        Assert.Equal("Clear History", With(("History", "Verlauf")).Get("Clear History"));
    }

    [Fact]
    public void An_empty_translation_counts_as_missing()
    {
        Assert.Equal("History", With(("History", "")).Get("History"));
        Assert.False(With(("History", "")).HasTranslation("History"));
    }

    [Fact]
    public void The_invariant_localizer_returns_every_key_unchanged()
    {
        Assert.Equal("Quit", Localizer.Invariant.Get("Quit"));
        Assert.Equal(0, Localizer.Invariant.Count);
    }

    [Fact]
    public void A_null_key_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => Localizer.Invariant.Get(null!));
    }

    // ---- Candidate locales ---------------------------------------------------------------

    [Fact]
    public void A_regional_culture_resolves_through_its_language_to_English()
    {
        // Least specific first, so later tables override earlier ones.
        Assert.Equal(
            ["en", "pt", "pt-BR"],
            LocaleResolver.CandidateLocales(new CultureInfo("pt-BR")));
    }

    [Fact]
    public void A_neutral_culture_resolves_to_itself_and_English()
    {
        Assert.Equal(["en", "de"], LocaleResolver.CandidateLocales(new CultureInfo("de")));
    }

    [Fact]
    public void English_is_not_listed_twice()
    {
        Assert.Equal(["en", "en-GB"], LocaleResolver.CandidateLocales(new CultureInfo("en-GB")));
        Assert.Equal(["en"], LocaleResolver.CandidateLocales(new CultureInfo("en")));
    }

    [Fact]
    public void The_invariant_culture_resolves_to_English_alone()
    {
        Assert.Equal(["en"], LocaleResolver.CandidateLocales(CultureInfo.InvariantCulture));
        Assert.Equal(["en"], LocaleResolver.CandidateLocales(null));
    }

    [Fact]
    public void A_script_specific_culture_resolves_through_its_chain()
    {
        IReadOnlyList<string> candidates = LocaleResolver.CandidateLocales(new CultureInfo("zh-Hans"));

        Assert.Equal("en", candidates[0]);
        Assert.Contains("zh-Hans", candidates);
        Assert.True(
            candidates.ToList().IndexOf("zh-Hans") > 0,
            "the specific locale should come after its fallbacks");
    }

    // ---- Building ---------------------------------------------------------------------------

    [Fact]
    public void A_more_specific_locale_overrides_a_less_specific_one()
    {
        Localizer localizer = LocaleResolver.Build(
            new CultureInfo("pt-BR"),
            locale => locale switch
            {
                "en" => new Dictionary<string, string> { ["History"] = "History (en)" },
                "pt-BR" => new Dictionary<string, string> { ["History"] = "Histórico" },
                _ => null,
            });

        Assert.Equal("Histórico", localizer.Get("History"));
        Assert.Equal("pt-BR", localizer.LocaleName);
    }

    [Fact]
    public void Entries_missing_from_the_specific_locale_come_from_the_fallback()
    {
        Localizer localizer = LocaleResolver.Build(
            new CultureInfo("de"),
            locale => locale switch
            {
                "en" => new Dictionary<string, string> { ["Settings…"] = "Settings…" },
                "de" => new Dictionary<string, string> { ["History"] = "Verlauf" },
                _ => null,
            });

        Assert.Equal("Verlauf", localizer.Get("History"));
        Assert.Equal("Settings…", localizer.Get("Settings…"));
    }

    [Fact]
    public void A_culture_with_no_shipped_table_still_gets_the_fallback()
    {
        Localizer localizer = LocaleResolver.Build(
            new CultureInfo("fi"),
            locale => locale == "en"
                ? new Dictionary<string, string> { ["History"] = "History" }
                : null);

        Assert.Equal("History", localizer.Get("History"));
        Assert.Equal("en", localizer.LocaleName);
    }

    [Fact]
    public void A_loader_that_supplies_nothing_yields_key_passthrough()
    {
        Localizer localizer = LocaleResolver.Build(new CultureInfo("de"), _ => null);

        Assert.Equal("History", localizer.Get("History"));
        Assert.Equal(0, localizer.Count);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => LocaleResolver.Build(null, null!));
        Assert.Throws<ArgumentNullException>(() => new Localizer(null!));
    }
}
