using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using CopyPasta.Core.Capture;
using CopyPasta.Core.Clipboard;
using CopyPasta.Core.Hotkeys;
using CopyPasta.Core.Menu;
using CopyPasta.Core.Paste;
using CopyPasta.Core.Updates;
using CopyPasta.Interop;

namespace CopyPasta.App;

/// <summary>
/// Everything the user can configure, as one serialisable object.
/// </summary>
/// <remarks>
/// <para>
/// Replaces macOS <c>UserDefaults</c> / <c>AppStorageValues</c>. The macOS version maps clean
/// Swift names onto legacy defaults keys (<c>inputPasteCommand</c>,
/// <c>numberOfItemsPlaceInline</c>); there is no legacy to honour on Windows, so the names here
/// are the clean ones and the indirection is gone.
/// </para>
/// <para>
/// Stored as JSON under <c>%APPDATA%</c> rather than in the registry: easy to inspect, easy to
/// back up, and it moves with the user's profile.
/// </para>
/// </remarks>
public sealed record AppSettings
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    // ---- Behaviour ----------------------------------------------------------------------

    /// <summary>Start with Windows. Port of <c>isLaunchAtLogin</c>.</summary>
    public bool LaunchAtLogin { get; init; }

    public bool PastesAutomatically { get; init; } = true;

    public bool ReordersClipsAfterPasting { get; init; } = true;

    public int MaximumHistoryCount { get; init; } = 30;

    public bool ClearsHistoryOnQuit { get; init; }

    /// <summary>Confirm before wiping the history. Port of <c>showsClearHistoryAlert</c>.</summary>
    public bool ShowsClearHistoryAlert { get; init; } = true;

    /// <summary>Wipe the history on a timer. Port of <c>clearsHistoryPeriodically</c>.</summary>
    public bool ClearsHistoryPeriodically { get; init; }

    /// <summary>
    /// How often, in seconds. Port of <c>historyClearInterval</c>, whose macOS default is one hour.
    /// </summary>
    public int HistoryClearIntervalSeconds { get; init; } = 3600;

    // ---- Capture -------------------------------------------------------------------------

    public bool IgnoresConcealedContent { get; init; }

    public bool IgnoresCloudClipboard { get; init; }

    public bool AllowsDuplicates { get; init; } = true;

    public bool OverwritesDuplicates { get; init; } = true;

    public List<string> DisabledContentTypes { get; init; } = [];

    public List<string> ExcludedApplications { get; init; } = [];

    /// <summary>
    /// Save screenshots to the history as they are taken. Port of <c>observesScreenshots</c>.
    /// </summary>
    public bool ObservesScreenshots { get; init; }

    /// <summary>
    /// Read text out of copied images so they can be searched.
    /// </summary>
    /// <remarks>
    /// macOS has no equivalent toggle — it always runs Vision. Recognition here depends on an
    /// installed OCR language pack and costs real CPU on a large screenshot, so it is worth being
    /// able to turn off.
    /// </remarks>
    public bool RecognizesTextInImages { get; init; } = true;

    /// <summary>
    /// Total size the stored clips may occupy, in megabytes. Zero disables the cap.
    /// </summary>
    /// <remarks>
    /// No macOS counterpart. macOS trims by row count alone, so a history of thirty 4K screenshots
    /// is thirty rows and several hundred megabytes — the gap the code review called out.
    /// </remarks>
    public int MaximumHistoryMegabytes { get; init; } = 256;

    // ---- Menu ----------------------------------------------------------------------------

    public int InlineItemLimit { get; init; }

    public int FolderItemLimit { get; init; } = 10;

    public bool StartsNumberingAtZero { get; init; }

    public bool ShowsNumbers { get; init; } = true;

    public bool AddsNumericAccelerators { get; init; }

    public int MaximumTitleLength { get; init; } = 20;

    public bool ShowsToolTips { get; init; } = true;

    public int MaximumToolTipLength { get; init; } = 200;

    /// <summary>
    /// Show a folder or text glyph beside menu entries. Port of <c>showsIconsInMenu</c>.
    /// </summary>
    public bool ShowsIconsInMenu { get; init; } = true;

    public bool ShowsImages { get; init; } = true;

    public bool ShowsColorPreviews { get; init; } = true;

    public bool ShowsClearHistoryCommand { get; init; } = true;

    public int ThumbnailWidth { get; init; } = 100;

    public int ThumbnailHeight { get; init; } = 32;

    public TrayGlyphStyle TrayGlyphStyle { get; init; } = TrayGlyphStyle.Light;

    /// <summary>Hide the tray icon entirely. Port of the macOS "hidden" menu-bar-icon option.</summary>
    public bool HidesTrayIcon { get; init; }

    // ---- Updates ---------------------------------------------------------------------------

    /// <summary>Look for new releases without being asked. Port of Sparkle's equivalent.</summary>
    public bool AutomaticallyChecksForUpdates { get; init; } = true;

    /// <summary>
    /// Seconds between automatic checks. Daily by default, matching macOS.
    /// </summary>
    /// <remarks>
    /// Clamped by <see cref="UpdateSchedule.EffectiveInterval"/> rather than validated here, so a
    /// hand-edited settings file cannot turn this into a request loop.
    /// </remarks>
    public int UpdateCheckIntervalSeconds { get; init; } = 86_400;

    /// <summary>
    /// When a check last completed, so the schedule survives a restart.
    /// </summary>
    /// <remarks>
    /// A tray app is restarted often — on sign-in, after an update, whenever the user quits it.
    /// Keeping this only in memory would mean checking on every single start.
    /// </remarks>
    public DateTimeOffset? LastUpdateCheckUtc { get; init; }

    /// <summary>The update policy as the scheduler wants it.</summary>
    public UpdatePolicy ToUpdatePolicy() =>
        new(AutomaticallyChecksForUpdates, TimeSpan.FromSeconds(UpdateCheckIntervalSeconds));

    // ---- Hotkeys ---------------------------------------------------------------------------

    /// <summary>
    /// Pops the full menu — history plus snippets plus commands.
    /// </summary>
    /// <remarks>
    /// macOS defaults these to ⌘⇧V, ⌘⌃V and ⌘⇧B. The obvious transliteration of the first is
    /// Ctrl+Shift+V, which would be a bad default here: a global hotkey intercepts the combination
    /// before any application sees it, and Ctrl+Shift+V is Windows' own idiom for "paste without
    /// formatting". Binding it globally would break that everywhere. Ctrl+Alt is the conventional
    /// Windows space for third-party global hotkeys, so the defaults live there instead.
    /// </remarks>
    public string? MainMenuHotkey { get; init; } = "Ctrl+Alt+V";

    public string? HistoryMenuHotkey { get; init; } = "Ctrl+Alt+H";

    public string? SnippetMenuHotkey { get; init; } = "Ctrl+Alt+B";

    /// <summary>Unbound by default, matching macOS.</summary>
    public string? EditSnippetsHotkey { get; init; }

    /// <summary>Unbound by default, matching macOS.</summary>
    public string? ClearHistoryHotkey { get; init; }

    /// <summary>
    /// Per-folder snippet hotkeys, keyed by folder id. Port of the macOS <c>folderKeyCombos</c>.
    /// </summary>
    public Dictionary<string, string> SnippetFolderHotkeys { get; init; } = [];

    /// <summary>
    /// The bindings as the hotkey service wants them. An unparseable string is treated as unbound
    /// rather than crashing the start-up.
    /// </summary>
    public IReadOnlyDictionary<string, KeyCombination?> ToHotkeyBindings() =>
        new Dictionary<string, KeyCombination?>
        {
            [HotkeyService.Actions.ShowMainMenu] = Parse(MainMenuHotkey),
            [HotkeyService.Actions.ShowHistoryMenu] = Parse(HistoryMenuHotkey),
            [HotkeyService.Actions.ShowSnippetMenu] = Parse(SnippetMenuHotkey),
            [HotkeyService.Actions.EditSnippets] = Parse(EditSnippetsHotkey),
            [HotkeyService.Actions.ClearHistory] = Parse(ClearHistoryHotkey),
        };

    private static KeyCombination? Parse(string? text) =>
        KeyCombination.TryParse(text, out KeyCombination combination) ? combination : null;

    // ---- Paste modifiers -------------------------------------------------------------------

    public bool PastesPlainTextWithModifier { get; init; } = true;

    public ModifierKey PlainTextModifier { get; init; } = ModifierKey.Shift;

    public bool DeletesWithModifier { get; init; }

    public ModifierKey DeleteModifier { get; init; } = ModifierKey.Control;

    public bool PastesAndDeletesWithModifier { get; init; }

    public ModifierKey PasteAndDeleteModifier { get; init; } = ModifierKey.Alt;

    // ---- Derived views ---------------------------------------------------------------------

    public CaptureSettings ToCaptureSettings() => new()
    {
        Filter = new ClipboardFilterOptions
        {
            EnabledTypes = ClipContentTypes.All
                .Where(type => !DisabledContentTypes.Contains(
                    type.ToSettingsKey(),
                    StringComparer.OrdinalIgnoreCase))
                .ToHashSet(),
            IgnoresConcealedContent = IgnoresConcealedContent,
            IgnoresCloudClipboard = IgnoresCloudClipboard,
        },
        ExcludedApplications = ExcludedApplications
            .Select(name => new ExcludedApplication(name))
            .ToArray(),
        AllowsDuplicates = AllowsDuplicates,
        OverwritesDuplicates = OverwritesDuplicates,
    };

    public MenuLayoutSettings ToMenuSettings() => new()
    {
        InlineItemLimit = InlineItemLimit,
        FolderItemLimit = FolderItemLimit,
        MaximumHistoryCount = MaximumHistoryCount,
        StartsNumberingAtZero = StartsNumberingAtZero,
        ShowsNumbers = ShowsNumbers,
        AddsNumericAccelerators = AddsNumericAccelerators,
        MaximumTitleLength = MaximumTitleLength,
        ShowsToolTips = ShowsToolTips,
        MaximumToolTipLength = MaximumToolTipLength,
        ShowsIcons = ShowsIconsInMenu,
        ShowsImages = ShowsImages,
        ShowsColorPreviews = ShowsColorPreviews,
        ShowsClearHistoryCommand = ShowsClearHistoryCommand,
    };

    public PasteSettings ToPasteSettings() => new()
    {
        PastesAutomatically = PastesAutomatically,
        PastePlainText = new ModifierAction(PastesPlainTextWithModifier, PlainTextModifier),
        DeleteWithoutPasting = new ModifierAction(DeletesWithModifier, DeleteModifier),
        PasteAndDelete = new ModifierAction(PastesAndDeletesWithModifier, PasteAndDeleteModifier),
    };

    public ThumbnailSize ToThumbnailSize() => new(ThumbnailWidth, ThumbnailHeight);

    // ---- Storage -----------------------------------------------------------------------------

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CopyPasta");

    public static string DefaultPath => Path.Combine(DefaultDirectory, "settings.json");

    /// <summary>
    /// Loads settings, falling back to defaults.
    /// </summary>
    /// <remarks>
    /// A corrupt or partially written file yields defaults rather than a failed start: a clipboard
    /// manager that refuses to launch because of one bad setting is worse than one that forgets a
    /// preference.
    /// </remarks>
    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;

        try
        {
            if (!File.Exists(path))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), SerializerOptions)
                   ?? new AppSettings();
        }
        catch (Exception exception) when (exception is IOException
                                             or JsonException
                                             or UnauthorizedAccessException)
        {
            return new AppSettings();
        }
    }

    public void Save(string? path = null)
    {
        path = path ?? DefaultPath;

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Write to a temporary file and move it into place, so an interrupted write cannot leave a
        // half-written settings file behind.
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, SerializerOptions));
        File.Move(temporary, path, overwrite: true);
    }
}
