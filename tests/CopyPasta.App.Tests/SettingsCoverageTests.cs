using System.Reflection;

namespace CopyPasta.App.Tests;

/// <summary>
/// The Phase 6 completion criterion: every setting macOS stores has a Windows counterpart or a
/// documented reason for omission.
/// </summary>
/// <remarks>
/// The audit table is only worth having if it cannot drift from the code, so these tests check the
/// named properties really exist and that nothing is left undocumented.
/// </remarks>
public class SettingsCoverageTests
{
    private static readonly HashSet<string> SettingsProperties = typeof(AppSettings)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(property => property.Name)
        .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void Every_ported_setting_names_a_property_that_actually_exists()
    {
        List<string> missing = SettingsCoverage.All
            .Where(setting => setting.WindowsProperty is not null)
            .Where(setting => !SettingsProperties.Contains(setting.WindowsProperty!))
            .Select(setting => $"{setting.MacOsName} -> {setting.WindowsProperty}")
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"The audit names properties that do not exist on AppSettings: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Every_ported_setting_has_a_property()
    {
        List<string> unmapped = SettingsCoverage.Ported
            .Where(setting => setting.WindowsProperty is null)
            .Select(setting => setting.MacOsName)
            .ToList();

        Assert.True(
            unmapped.Count == 0,
            $"Marked as ported but mapped to nothing: {string.Join(", ", unmapped)}");
    }

    [Fact]
    public void Everything_not_ported_explains_itself()
    {
        List<string> undocumented = SettingsCoverage.NotPorted
            .Where(setting => string.IsNullOrWhiteSpace(setting.Note))
            .Select(setting => setting.MacOsName)
            .ToList();

        Assert.True(
            undocumented.Count == 0,
            $"Not ported and not explained: {string.Join(", ", undocumented)}");
    }

    [Fact]
    public void The_audit_lists_each_macOS_setting_once()
    {
        List<string> duplicates = SettingsCoverage.All
            .GroupBy(setting => setting.MacOsName, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, $"Listed twice: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void The_audit_covers_every_setting_macOS_stores()
    {
        // Transcribed from AppStorageValues.swift. If the macOS source gains a setting, this list
        // is where the port notices.
        string[] macOsSettings =
        [
            "isLaunchAtLogin", "suppressesLoginItemAlert", "pastesAutomatically",
            "reordersClipsAfterPasting", "collectsCrashReports", "startsMenuItemTitlesAtZero",
            "showsClearHistoryAlert", "clearsHistoryOnQuit", "clearsHistoryPeriodically",
            "showsClearHistoryMenuItem", "showsIconsInMenu", "marksMenuItemsWithNumbers",
            "showsToolTipsOnMenuItems", "showsImagesInMenu", "addsNumericKeyEquivalents",
            "overwritesDuplicateHistory", "allowsDuplicateHistory", "showsColorPreviewInMenu",
            "ignoresConcealedPasteboardTypes", "ignoresUniversalClipboard",
            "pastesPlainTextWithModifier", "deletesHistoryWithModifier",
            "pastesAndDeletesHistoryWithModifier", "observesScreenshots",
            "maximumHistoryCount", "statusItemDisplayMode", "menuIconSize",
            "maximumMenuItemTitleLength", "inlineMenuItemLimit", "folderMenuItemLimit",
            "maximumToolTipLength", "thumbnailWidth", "thumbnailHeight",
            "plainTextPasteModifier", "historyDeletionModifier", "pasteAndDeleteHistoryModifier",
            "pasteboardTypeSettings", "historyClearInterval",
            "mainKeyCombo", "historyKeyCombo", "snippetKeyCombo", "editSnippetsKeyCombo",
            "clearHistoryKeyCombo", "folderKeyCombos", "excludedApplications",
        ];

        HashSet<string> audited = SettingsCoverage.All
            .Select(setting => setting.MacOsName)
            .ToHashSet(StringComparer.Ordinal);

        string[] unaudited = macOsSettings.Where(name => !audited.Contains(name)).ToArray();
        Assert.True(
            unaudited.Length == 0,
            $"macOS settings with no entry in the audit: {string.Join(", ", unaudited)}");

        string[] invented = audited.Where(name => !macOsSettings.Contains(name)).ToArray();
        Assert.True(
            invented.Length == 0,
            $"Audit entries that do not exist in macOS: {string.Join(", ", invented)}");
    }

    [Fact]
    public void Most_settings_are_actually_ported()
    {
        // A sanity floor: if this drops, something has regressed rather than been documented.
        Assert.True(
            SettingsCoverage.Ported.Count() >= 40,
            SettingsCoverage.Summary());
    }
}
