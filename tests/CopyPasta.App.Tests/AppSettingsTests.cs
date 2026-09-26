using System.IO;
using CopyPasta.Core.Capture;
using CopyPasta.Core.Clipboard;
using CopyPasta.Core.Hotkeys;
using CopyPasta.Core.Paste;
using CopyPasta.Interop;

namespace CopyPasta.App.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"copypasta-settings-{Guid.NewGuid():n}");

    private string Path_ => Path.Combine(_directory, "settings.json");

    public AppSettingsTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    // ---- Persistence --------------------------------------------------------------------

    [Fact]
    public void Settings_survive_a_save_and_load()
    {
        AppSettings original = new()
        {
            MaximumHistoryCount = 77,
            InlineItemLimit = 5,
            StartsNumberingAtZero = true,
            PlainTextModifier = ModifierKey.Alt,
            TrayGlyphStyle = TrayGlyphStyle.Dark,
            MainMenuHotkey = "Ctrl+Shift+F9",
            ExcludedApplications = ["KeePass"],
            DisabledContentTypes = ["Image"],
            SnippetFolderHotkeys = { ["abc"] = "Ctrl+Alt+1" },
        };

        original.Save(Path_);
        AppSettings loaded = AppSettings.Load(Path_);

        Assert.Equal(77, loaded.MaximumHistoryCount);
        Assert.Equal(5, loaded.InlineItemLimit);
        Assert.True(loaded.StartsNumberingAtZero);
        Assert.Equal(ModifierKey.Alt, loaded.PlainTextModifier);
        Assert.Equal(TrayGlyphStyle.Dark, loaded.TrayGlyphStyle);
        Assert.Equal("Ctrl+Shift+F9", loaded.MainMenuHotkey);
        Assert.Equal(["KeePass"], loaded.ExcludedApplications);
        Assert.Equal(["Image"], loaded.DisabledContentTypes);
        Assert.Equal("Ctrl+Alt+1", loaded.SnippetFolderHotkeys["abc"]);
    }

    [Fact]
    public void A_missing_file_yields_defaults()
    {
        AppSettings loaded = AppSettings.Load(Path.Combine(_directory, "nope.json"));

        Assert.Equal(30, loaded.MaximumHistoryCount);
        Assert.True(loaded.PastesAutomatically);
    }

    [Fact]
    public void A_corrupt_file_yields_defaults_rather_than_refusing_to_start()
    {
        // A clipboard manager that will not launch because of one bad setting is worse than one
        // that forgets a preference.
        File.WriteAllText(Path_, "{ this is not json");

        Assert.Equal(30, AppSettings.Load(Path_).MaximumHistoryCount);
    }

    [Fact]
    public void An_unknown_property_is_ignored()
    {
        // A settings file written by a newer build must not break an older one.
        File.WriteAllText(Path_, """{ "MaximumHistoryCount": 42, "SomeFutureSetting": true }""");

        Assert.Equal(42, AppSettings.Load(Path_).MaximumHistoryCount);
    }

    [Fact]
    public void Saving_creates_the_directory()
    {
        string nested = Path.Combine(_directory, "deep", "settings.json");

        new AppSettings().Save(nested);

        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Saving_over_an_existing_file_replaces_it_atomically()
    {
        new AppSettings { MaximumHistoryCount = 10 }.Save(Path_);
        new AppSettings { MaximumHistoryCount = 20 }.Save(Path_);

        Assert.Equal(20, AppSettings.Load(Path_).MaximumHistoryCount);
        Assert.False(File.Exists(Path_ + ".tmp"), "the temporary file should not be left behind");
    }

    // ---- Derived views ---------------------------------------------------------------------

    [Fact]
    public void Disabled_content_types_are_excluded_from_capture()
    {
        CaptureSettings capture = new AppSettings
        {
            DisabledContentTypes = ["Image", "PDF"],
        }.ToCaptureSettings();

        Assert.DoesNotContain(ClipContentType.Image, capture.Filter.EnabledTypes);
        Assert.DoesNotContain(ClipContentType.Pdf, capture.Filter.EnabledTypes);
        Assert.Contains(ClipContentType.Text, capture.Filter.EnabledTypes);
    }

    [Fact]
    public void Every_content_type_is_enabled_by_default()
    {
        Assert.Equal(
            ClipContentTypes.All.Count,
            new AppSettings().ToCaptureSettings().Filter.EnabledTypes.Count);
    }

    [Fact]
    public void Excluded_applications_reach_the_capture_settings()
    {
        CaptureSettings capture = new AppSettings
        {
            ExcludedApplications = ["KeePass", "1Password"],
        }.ToCaptureSettings();

        Assert.Equal(2, capture.ExcludedApplications.Count);
        Assert.Equal("KeePass", capture.ExcludedApplications[0].Identifier);
    }

    [Fact]
    public void Paste_modifiers_reach_the_paste_settings()
    {
        PasteSettings paste = new AppSettings
        {
            PastesPlainTextWithModifier = true,
            PlainTextModifier = ModifierKey.Control,
            DeletesWithModifier = false,
        }.ToPasteSettings();

        Assert.True(paste.PastePlainText.IsEnabled);
        Assert.Equal(ModifierKey.Control, paste.PastePlainText.Modifier);
        Assert.False(paste.DeleteWithoutPasting.IsEnabled);
    }

    // ---- Hotkey bindings -----------------------------------------------------------------------

    [Fact]
    public void The_default_hotkeys_parse()
    {
        IReadOnlyDictionary<string, KeyCombination?> bindings =
            new AppSettings().ToHotkeyBindings();

        Assert.NotNull(bindings[HotkeyService.Actions.ShowMainMenu]);
        Assert.NotNull(bindings[HotkeyService.Actions.ShowHistoryMenu]);
        Assert.NotNull(bindings[HotkeyService.Actions.ShowSnippetMenu]);

        // Unbound by default, matching macOS.
        Assert.Null(bindings[HotkeyService.Actions.EditSnippets]);
        Assert.Null(bindings[HotkeyService.Actions.ClearHistory]);
    }

    [Fact]
    public void The_default_hotkeys_avoid_the_platform_paste_shortcut()
    {
        // Ctrl+Shift+V is Windows' own "paste without formatting"; a global hotkey would
        // intercept it before any application saw it.
        AppSettings settings = new();

        Assert.DoesNotContain(
            "Ctrl+Shift+V",
            new[] { settings.MainMenuHotkey, settings.HistoryMenuHotkey, settings.SnippetMenuHotkey });
    }

    [Fact]
    public void An_unparseable_hotkey_becomes_unbound_rather_than_crashing_the_start()
    {
        IReadOnlyDictionary<string, KeyCombination?> bindings = new AppSettings
        {
            MainMenuHotkey = "this is not a shortcut",
        }.ToHotkeyBindings();

        Assert.Null(bindings[HotkeyService.Actions.ShowMainMenu]);
    }

    [Fact]
    public void A_hotkey_survives_a_round_trip_through_the_settings_file()
    {
        KeyCombination combination = new(
            VirtualKeys.V,
            HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift);

        new AppSettings { MainMenuHotkey = combination.ToString() }.Save(Path_);

        Assert.Equal(
            combination,
            AppSettings.Load(Path_).ToHotkeyBindings()[HotkeyService.Actions.ShowMainMenu]);
    }

    // ---- Menu and thumbnail views -----------------------------------------------------------------

    [Fact]
    public void Menu_settings_carry_the_layout_values()
    {
        AppSettings settings = new()
        {
            InlineItemLimit = 7,
            FolderItemLimit = 12,
            MaximumHistoryCount = 50,
            ShowsNumbers = false,
        };

        Assert.Equal(7, settings.ToMenuSettings().InlineItemLimit);
        Assert.Equal(12, settings.ToMenuSettings().FolderItemLimit);
        Assert.Equal(50, settings.ToMenuSettings().MaximumHistoryCount);
        Assert.False(settings.ToMenuSettings().ShowsNumbers);
    }

    [Fact]
    public void Thumbnail_size_carries_through()
    {
        AppSettings settings = new() { ThumbnailWidth = 120, ThumbnailHeight = 40 };

        Assert.Equal(120, settings.ToThumbnailSize().Width);
        Assert.Equal(40, settings.ToThumbnailSize().Height);
    }
}
