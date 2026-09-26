namespace CopyPasta.App;

/// <summary>What happened to a macOS setting in this port.</summary>
public enum CoverageStatus
{
    /// <summary>There is an equivalent setting and the app acts on it.</summary>
    Ported,

    /// <summary>The setting exists but something it depends on is not built yet.</summary>
    Pending,

    /// <summary>Deliberately not carried over; <see cref="SettingCoverage.Note"/> says why.</summary>
    Omitted,
}

/// <param name="MacOsName">The property name in the macOS <c>AppStorageValues.swift</c>.</param>
/// <param name="WindowsProperty">
/// The matching property on <see cref="AppSettings"/>, or null when there is none.
/// </param>
/// <param name="Note">Why, for anything not straightforwardly ported.</param>
public sealed record SettingCoverage(
    string MacOsName,
    string? WindowsProperty,
    CoverageStatus Status,
    string? Note = null);

/// <summary>
/// The settings audit: every value macOS stores, and what became of it here.
/// </summary>
/// <remarks>
/// This is the Phase 6 completion criterion in executable form — "every setting in
/// AppStorageValues.swift has a Windows counterpart or a documented reason for omission". A test
/// checks that each named property really exists on <see cref="AppSettings"/>, so the table cannot
/// quietly drift away from the code it describes.
/// </remarks>
public static class SettingsCoverage
{
    public static IReadOnlyList<SettingCoverage> All { get; } =
    [
        // ---- Behaviour -------------------------------------------------------------------
        new("isLaunchAtLogin", nameof(AppSettings.LaunchAtLogin), CoverageStatus.Ported),
        new("pastesAutomatically", nameof(AppSettings.PastesAutomatically), CoverageStatus.Ported),
        new("reordersClipsAfterPasting", nameof(AppSettings.ReordersClipsAfterPasting), CoverageStatus.Ported),
        new("maximumHistoryCount", nameof(AppSettings.MaximumHistoryCount), CoverageStatus.Ported),
        new("clearsHistoryOnQuit", nameof(AppSettings.ClearsHistoryOnQuit), CoverageStatus.Ported),
        new("clearsHistoryPeriodically", nameof(AppSettings.ClearsHistoryPeriodically), CoverageStatus.Ported),
        new("historyClearInterval", nameof(AppSettings.HistoryClearIntervalSeconds), CoverageStatus.Ported),
        new("showsClearHistoryAlert", nameof(AppSettings.ShowsClearHistoryAlert), CoverageStatus.Ported),

        new(
            "suppressesLoginItemAlert",
            null,
            CoverageStatus.Omitted,
            "Suppresses a first-run prompt macOS shows asking to launch at login. This port does " +
            "not nag: the setting is simply off until the user turns it on, so there is nothing " +
            "to suppress."),

        new(
            "collectsCrashReports",
            null,
            CoverageStatus.Omitted,
            "Firebase Analytics and Crashlytics are not carried over — see PORTING_PLAN.md §2. " +
            "With no telemetry there is nothing to opt out of."),

        // ---- Capture ----------------------------------------------------------------------
        new("allowsDuplicateHistory", nameof(AppSettings.AllowsDuplicates), CoverageStatus.Ported),
        new("overwritesDuplicateHistory", nameof(AppSettings.OverwritesDuplicates), CoverageStatus.Ported),
        new("ignoresConcealedPasteboardTypes", nameof(AppSettings.IgnoresConcealedContent), CoverageStatus.Ported),
        new("ignoresUniversalClipboard", nameof(AppSettings.IgnoresCloudClipboard), CoverageStatus.Ported),
        new("pasteboardTypeSettings", nameof(AppSettings.DisabledContentTypes), CoverageStatus.Ported),
        new("excludedApplications", nameof(AppSettings.ExcludedApplications), CoverageStatus.Ported),

        new("observesScreenshots", nameof(AppSettings.ObservesScreenshots), CoverageStatus.Ported,
            "macOS queries Spotlight; Windows has no equivalent index, so this watches " +
            "%USERPROFILE%\\Pictures\\Screenshots, where Win+PrtScn and the Snipping Tool save."),

        // ---- Menu -------------------------------------------------------------------------
        new("inlineMenuItemLimit", nameof(AppSettings.InlineItemLimit), CoverageStatus.Ported),
        new("folderMenuItemLimit", nameof(AppSettings.FolderItemLimit), CoverageStatus.Ported),
        new("maximumMenuItemTitleLength", nameof(AppSettings.MaximumTitleLength), CoverageStatus.Ported),
        new("startsMenuItemTitlesAtZero", nameof(AppSettings.StartsNumberingAtZero), CoverageStatus.Ported),
        new("marksMenuItemsWithNumbers", nameof(AppSettings.ShowsNumbers), CoverageStatus.Ported),
        new("addsNumericKeyEquivalents", nameof(AppSettings.AddsNumericAccelerators), CoverageStatus.Ported),
        new("showsToolTipsOnMenuItems", nameof(AppSettings.ShowsToolTips), CoverageStatus.Ported),
        new("maximumToolTipLength", nameof(AppSettings.MaximumToolTipLength), CoverageStatus.Ported),
        new("showsImagesInMenu", nameof(AppSettings.ShowsImages), CoverageStatus.Ported),
        new("showsColorPreviewInMenu", nameof(AppSettings.ShowsColorPreviews), CoverageStatus.Ported),
        new("showsClearHistoryMenuItem", nameof(AppSettings.ShowsClearHistoryCommand), CoverageStatus.Ported),
        new("thumbnailWidth", nameof(AppSettings.ThumbnailWidth), CoverageStatus.Ported),
        new("thumbnailHeight", nameof(AppSettings.ThumbnailHeight), CoverageStatus.Ported),
        new("statusItemDisplayMode", nameof(AppSettings.TrayGlyphStyle), CoverageStatus.Ported,
            "macOS folds 'hidden' into the same three-way setting; here the visibility half is " +
            nameof(AppSettings.HidesTrayIcon) + "."),

        new("showsIconsInMenu", nameof(AppSettings.ShowsIconsInMenu), CoverageStatus.Ported,
            "Originally omitted for lack of artwork — Clipy's icons are copyrighted separately " +
            "from its MIT grant. CopyPasta now has its own, so the setting is restored."),

        new(
            "menuIconSize",
            null,
            CoverageStatus.Omitted,
            "Sizes the macOS menu-bar icon. Windows sizes notification-area icons itself from the " +
            "system metric and the display's DPI, so there is nothing for a user to set."),

        // ---- Paste modifiers -----------------------------------------------------------------
        new("pastesPlainTextWithModifier", nameof(AppSettings.PastesPlainTextWithModifier), CoverageStatus.Ported),
        new("plainTextPasteModifier", nameof(AppSettings.PlainTextModifier), CoverageStatus.Ported),
        new("deletesHistoryWithModifier", nameof(AppSettings.DeletesWithModifier), CoverageStatus.Ported),
        new("historyDeletionModifier", nameof(AppSettings.DeleteModifier), CoverageStatus.Ported),
        new("pastesAndDeletesHistoryWithModifier", nameof(AppSettings.PastesAndDeletesWithModifier), CoverageStatus.Ported),
        new("pasteAndDeleteHistoryModifier", nameof(AppSettings.PasteAndDeleteModifier), CoverageStatus.Ported),

        // ---- Hotkeys ---------------------------------------------------------------------------
        new("mainKeyCombo", nameof(AppSettings.MainMenuHotkey), CoverageStatus.Ported),
        new("historyKeyCombo", nameof(AppSettings.HistoryMenuHotkey), CoverageStatus.Ported),
        new("snippetKeyCombo", nameof(AppSettings.SnippetMenuHotkey), CoverageStatus.Ported),
        new("editSnippetsKeyCombo", nameof(AppSettings.EditSnippetsHotkey), CoverageStatus.Ported),
        new("clearHistoryKeyCombo", nameof(AppSettings.ClearHistoryHotkey), CoverageStatus.Ported),
        new("folderKeyCombos", nameof(AppSettings.SnippetFolderHotkeys), CoverageStatus.Ported),
    ];

    /// <summary>
    /// Settings this port adds that macOS has no equivalent for.
    /// </summary>
    /// <remarks>
    /// Listed alongside the audit so the Beta pane can show both directions: what did not come
    /// over, and what was added because the platform or the original needed it.
    /// </remarks>
    public static IReadOnlyList<SettingCoverage> Additions { get; } =
    [
        new(
            "(none)",
            nameof(AppSettings.MaximumHistoryMegabytes),
            CoverageStatus.Ported,
            "A byte budget for the stored history. macOS trims by row count alone, so thirty 4K " +
            "screenshots are thirty rows and several hundred megabytes."),

        new(
            "(none)",
            nameof(AppSettings.RecognizesTextInImages),
            CoverageStatus.Ported,
            "macOS always runs Vision. Windows OCR depends on an installed language pack and costs " +
            "real CPU on a large screenshot, so it can be turned off."),
    ];

    public static IEnumerable<SettingCoverage> Ported =>
        All.Where(setting => setting.Status == CoverageStatus.Ported);

    public static IEnumerable<SettingCoverage> NotPorted =>
        All.Where(setting => setting.Status != CoverageStatus.Ported);

    /// <summary>A summary line, for the About pane and the log.</summary>
    public static string Summary() =>
        $"{Ported.Count()} of {All.Count} macOS settings ported, " +
        $"{All.Count(setting => setting.Status == CoverageStatus.Pending)} pending, " +
        $"{All.Count(setting => setting.Status == CoverageStatus.Omitted)} deliberately omitted.";
}
