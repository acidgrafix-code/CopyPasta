using System.IO;
using CopyPasta.Core.Capture;
using CopyPasta.Core.Clipboard;
using CopyPasta.Core.Hotkeys;
using CopyPasta.Core.Localization;
using CopyPasta.Core.Menu;
using CopyPasta.Core.Ocr;
using CopyPasta.Core.Paste;
using CopyPasta.App.Settings;
using CopyPasta.App.Snippets;
using CopyPasta.Core.Snippets;
using CopyPasta.Core.Updates;
using CopyPasta.App.Updates;
using CopyPasta.Data;
using CopyPasta.Interop;

namespace CopyPasta.App;

/// <summary>
/// Wires the pieces together: capture, storage, menu, paste.
/// </summary>
/// <remarks>
/// The counterpart of the macOS <c>AppDelegate</c> plus <c>MenuManager</c>, minus the parts that
/// belong to later phases (snippets, the settings window).
/// </remarks>
public sealed class TrayApplication : IDisposable
{
    private AppSettings _settings;
    private readonly ClipDatabase _database;
    private readonly SqliteClipStore _store;
    private readonly SqliteSnippetStore _snippets;
    private readonly MessageWindow _window;
    private readonly ClipboardMonitor _monitor;
    private readonly TrayIcon _trayIcon;
    private readonly PopupMenuRenderer _menuRenderer;
    private readonly ClipCaptureService _capture;
    private readonly PasteService _paste;
    private readonly Win32HotkeyRegistrar _hotkeyRegistrar;
    private readonly HotkeyService _hotkeys;
    private readonly ClipboardFormatRegistry _formats = new();
    private readonly Localizer _localizer = AppLocalization.Load();
    private readonly Action _requestQuit;
    private readonly UpdateService _updates = new(UpdateRepositoryUrl);

    private SnippetEditorWindow? _snippetEditor;
    private SettingsWindow? _settingsWindow;
    private OcrQueue? _ocr;
    private ScreenshotWatcher? _screenshots;
    private Timer? _historyClearTimer;
    private Timer? _updateTimer;
    private bool _disposed;

    /// <summary>Where releases are published, and so where updates are looked for.</summary>
    private const string UpdateRepositoryUrl = "https://github.com/acidgrafix-code/CopyPasta";

    public TrayApplication(AppSettings settings, string databasePath, Action requestQuit)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(requestQuit);

        _settings = settings;
        _requestQuit = requestQuit;

        _database = new ClipDatabase(databasePath);
        _database.Migrate();
        _store = new SqliteClipStore(_database);
        _snippets = new SqliteSnippetStore(_database);

        // One message window serves both the clipboard monitor and the tray icon.
        _window = new MessageWindow("CopyPasta.App");
        _monitor = new ClipboardMonitor(_window);
        _trayIcon = new TrayIcon(_window, "CopyPasta");
        _menuRenderer = new PopupMenuRenderer();

        Win32ClipboardSource clipboard = new(_formats, _window.Handle);
        Win32ClipboardWriter writer = new(_formats, _window.Handle);

        _capture = new ClipCaptureService(clipboard, _store, new ForegroundApplication())
        {
            Settings = settings.ToCaptureSettings(),
        };

        _paste = new PasteService(
            writer,
            new InputSender(),
            new WindowFocus(),
            new ModifierKeys(),
            _capture,
            _store)
        {
            Settings = settings.ToPasteSettings(),
        };

        _hotkeyRegistrar = new Win32HotkeyRegistrar(_window);
        _hotkeys = new HotkeyService(_hotkeyRegistrar);
    }

    /// <summary>Raised for anything worth logging. Keeps this class free of a logger dependency.</summary>
    public event EventHandler<string>? Trace;

    public void Start()
    {
        _monitor.ClipboardChanged += OnClipboardChanged;
        _monitor.UnhandledMonitorException += (_, exception) => Log($"monitor handler failed: {exception.Message}");
        _monitor.Start();

        _trayIcon.Clicked += OnTrayClicked;

        if (!_settings.HidesTrayIcon)
        {
            _trayIcon.Show(TrayIconGlyph.Create(_settings.TrayGlyphStyle));

            // Worth recording: a build without artwork silently falls back to a drawn placeholder,
            // and that is not something to discover from a screenshot.
            Log(TrayIconGlyph.HasArtwork(_settings.TrayGlyphStyle)
                ? $"tray icon loaded from artwork ({_settings.TrayGlyphStyle})"
                : $"tray icon using the drawn placeholder ({_settings.TrayGlyphStyle}) — artwork missing");
        }

        RegisterHotkeys();
        ApplyHistoryClearTimer(_settings);
        StartUpdateTimer();
        StartTextRecognition();
        ApplyScreenshotWatcher(_settings);

        // Rewritten on every start so a stale entry left by a move points at the current copy.
        LaunchAtLogin.Apply(LaunchAtLoginValueName, ExecutablePath, _settings.LaunchAtLogin);

        // Capture whatever is already on the clipboard so the first menu is not empty.
        Capture();

        Log($"started; {_store.Count()} clips in {_database.Description}");
    }

    /// <summary>Port of <c>applicationWillTerminate</c>.</summary>
    public void Shutdown()
    {
        if (_settings.ClearsHistoryOnQuit)
        {
            _store.DeleteAll();
            Log("history cleared on quit");
        }
    }

    private void OnClipboardChanged(object? sender, EventArgs e) => Capture();

    private void Capture()
    {
        CaptureResult result = _capture.Capture();

        if (!result.WasCaptured)
        {
            if (result.Outcome != CaptureOutcome.Unchanged)
            {
                Log($"capture skipped: {result.Outcome}");
            }

            return;
        }

        AfterCapture(result.ClipId!);
        Log($"captured {result.ClipId?[..12]}");
    }

    /// <summary>The work that follows any newly stored clip, however it arrived.</summary>
    private void AfterCapture(string clipId)
    {
        GenerateThumbnail(clipId);

        if (_settings.RecognizesTextInImages)
        {
            _ocr?.Enqueue(clipId);
        }

        TrimHistory();
    }

    /// <summary>Saves a screenshot file to the history. Port of the macOS screenshot observer.</summary>
    private void CaptureScreenshot(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);

            ClipboardFormat format = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => ClipboardFormat.Png,
                ".jpg" or ".jpeg" => ClipboardFormat.Jfif,
                _ => ClipboardFormat.Png,
            };

            CaptureResult result = _capture.CaptureImage(format, bytes);

            if (!result.WasCaptured)
            {
                Log($"screenshot skipped: {result.Outcome}");
                return;
            }

            AfterCapture(result.ClipId!);
            Log($"captured screenshot {Path.GetFileName(path)} ({bytes.Length:N0} bytes)");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log($"could not read screenshot {Path.GetFileName(path)}: {exception.Message}");
        }
    }

    /// <summary>
    /// Builds and stores the menu preview for a freshly captured clip.
    /// </summary>
    /// <remarks>
    /// Done once at capture time rather than on every menu build: the menu is rebuilt on each
    /// popup, and rescaling a screenshot thirty times per popup would be visible.
    /// </remarks>
    private void GenerateThumbnail(string clipId)
    {
        if (!_settings.ShowsImages && !_settings.ShowsColorPreviews)
        {
            return;
        }

        try
        {
            ClipContent? content = _store.FetchContent(clipId);
            if (content is null)
            {
                return;
            }

            ClipThumbnail? thumbnail = ThumbnailRenderer.Create(content, _settings.ToThumbnailSize());
            if (thumbnail is not null)
            {
                _store.SaveThumbnail(clipId, thumbnail.Kind.ToString(), thumbnail.Data);
            }
        }
        catch (Exception exception)
        {
            // A preview is a nicety; never let it cost the clip.
            Log($"thumbnail failed for {clipId[..12]}: {exception.Message}");
        }
    }

    /// <summary>
    /// Enforces both caps: the number of clips, and the bytes they occupy.
    /// </summary>
    /// <remarks>
    /// The byte cap has no macOS counterpart. A count alone says nothing about size when one clip
    /// can be a line of text and the next a full-screen bitmap.
    /// </remarks>
    private void TrimHistory()
    {
        int removed = _store.DeleteOverflowing(CurrentOrder, _settings.MaximumHistoryCount);
        if (removed > 0)
        {
            Log($"trimmed {removed} clips over the {_settings.MaximumHistoryCount} limit");
        }

        if (_settings.MaximumHistoryMegabytes <= 0)
        {
            return;
        }

        long budget = (long)_settings.MaximumHistoryMegabytes * 1024 * 1024;
        int oversized = _store.DeleteOverflowingBytes(CurrentOrder, budget);

        if (oversized > 0)
        {
            Log($"trimmed {oversized} clips over the {_settings.MaximumHistoryMegabytes} MB budget");
        }
    }

    /// <summary>Menu captions and commands in the user's language.</summary>
    private MenuStrings MenuText => AppLocalization.MenuStrings(_localizer);

    private ClipOrder CurrentOrder =>
        _settings.ReordersClipsAfterPasting ? ClipOrder.UpdatedAt : ClipOrder.CreatedAt;

    /// <summary>
    /// Registers the configured hotkeys and reports every one that did not take.
    /// </summary>
    /// <remarks>
    /// Conflicts are logged individually rather than aggregated: "Ctrl+Alt+V is already in use" is
    /// actionable, "some hotkeys failed" is not. macOS discards the registration result entirely,
    /// so a taken combination there is indistinguishable from a broken app.
    /// </remarks>
    private void RegisterHotkeys()
    {
        _hotkeys.Triggered += OnHotkey;
        _hotkeys.HandlerFailed += (_, exception) => Log($"hotkey handler failed: {exception.Message}");

        foreach (HotkeyResult result in _hotkeys.Apply(_settings.ToHotkeyBindings()))
        {
            // Unbound actions are the normal state for two of the five; no need to narrate them.
            if (result.Status != HotkeyStatus.Unbound)
            {
                Log(result.ToString());
            }
        }

        RegisterSnippetFolderHotkeys();
    }

    /// <summary>
    /// Binds each snippet folder that has a hotkey configured.
    /// </summary>
    /// <remarks>
    /// Port of the macOS per-folder snippet hotkeys, which key their bindings by folder UUID. The
    /// same scheme works here because <c>HotkeyService</c> takes string action ids — a folder's id
    /// <em>is</em> its action id, so no second registration mechanism is needed. A binding for a
    /// folder that no longer exists is dropped, matching the macOS cleanup.
    /// </remarks>
    private void RegisterSnippetFolderHotkeys()
    {
        if (_settings.SnippetFolderHotkeys.Count == 0)
        {
            return;
        }

        HashSet<Guid> existing = _snippets
            .FetchFolderDetails()
            .Select(detail => detail.Folder.Id)
            .ToHashSet();

        foreach ((string folderId, string combination) in _settings.SnippetFolderHotkeys)
        {
            if (!Guid.TryParse(folderId, out Guid id) || !existing.Contains(id))
            {
                Log($"snippet folder hotkey for {folderId} ignored: no such folder");
                continue;
            }

            if (!KeyCombination.TryParse(combination, out KeyCombination parsed))
            {
                Log($"snippet folder hotkey '{combination}' could not be understood");
                continue;
            }

            HotkeyResult result = _hotkeys.Bind(id.ToString(), parsed);
            if (result.Status != HotkeyStatus.Unbound)
            {
                Log(result.ToString());
            }
        }
    }

    private void OnHotkey(object? sender, string actionId)
    {
        switch (actionId)
        {
            case HotkeyService.Actions.ShowMainMenu:
                ShowMenu();
                break;

            case HotkeyService.Actions.ShowHistoryMenu:
                ShowMenu(historyOnly: true);
                break;

            case HotkeyService.Actions.ShowSnippetMenu:
                ShowSnippetMenu();
                break;

            case HotkeyService.Actions.EditSnippets:
                RunCommand(MenuCommandKind.EditSnippets);
                break;

            case HotkeyService.Actions.ClearHistory:
                RunCommand(MenuCommandKind.ClearHistory);
                break;

            default:
                // A snippet folder's own hotkey: the action id is the folder id.
                if (Guid.TryParse(actionId, out Guid folderId))
                {
                    ShowSnippetFolderMenu(folderId);
                }
                else
                {
                    Log($"no handler for hotkey action {actionId}");
                }

                break;
        }
    }

    private void OnTrayClicked(object? sender, TrayClick click) => ShowMenu();

    /// <summary>Pops the snippet-only menu, for the snippet hotkey.</summary>
    public void ShowSnippetMenu()
    {
        IReadOnlyList<SnippetFolderDetail> folders = _snippets.FetchFolderDetails();

        if (folders.All(detail => !detail.Folder.IsEnabled))
        {
            // An empty popup looks like a broken app, so say nothing rather than show nothing.
            Log("snippet menu skipped: no enabled folders");
            return;
        }

        FocusToken target = _paste.CaptureTarget();
        ClipMenu menu = MenuBuilder.BuildSnippetMenu(folders, _settings.ToMenuSettings(), MenuText);

        Log($"menu opened (snippets, {folders.Count} folders)");
        HandleSelection(_menuRenderer.Show(menu, _window.Handle), target);
    }

    /// <summary>
    /// Pops one folder's snippets. Port of macOS <c>popUpSnippetFolder</c>.
    /// </summary>
    private void ShowSnippetFolderMenu(Guid folderId)
    {
        SnippetFolderDetail? folder = _snippets.FetchFolderDetail(folderId);

        if (folder is null)
        {
            // The folder was deleted since its hotkey was registered; release the binding rather
            // than leaving a combination reserved for something that no longer exists.
            Log($"snippet folder {folderId} no longer exists; releasing its hotkey");
            _hotkeys.Unbind(folderId.ToString());
            return;
        }

        if (!folder.Folder.IsEnabled)
        {
            Log($"snippet folder '{folder.Folder.Title}' is disabled");
            return;
        }

        FocusToken target = _paste.CaptureTarget();
        ClipMenu menu = MenuBuilder.BuildSnippetFolderMenu(folder, _settings.ToMenuSettings());

        Log($"menu opened (folder '{folder.Folder.Title}')");
        HandleSelection(_menuRenderer.Show(menu, _window.Handle), target);
    }

    /// <summary>Builds and shows the tray menu, then acts on what the user chose.</summary>
    /// <param name="historyOnly">
    /// Show only the history, without the snippet section or the command block. Port of the macOS
    /// separate history menu.
    /// </param>
    public void ShowMenu(bool historyOnly = false)
    {
        // Captured before the menu opens: showing it makes us the foreground process, so by the
        // time the user picks an item the original target is long gone.
        FocusToken target = _paste.CaptureTarget();

        MenuLayoutSettings layout = _settings.ToMenuSettings();
        IReadOnlyList<HistoryClip> history = LoadHistory();

        ClipMenu menu = historyOnly
            ? MenuBuilder.BuildHistoryMenu(history, layout, MenuText)
            : MenuBuilder.BuildMainMenu(history, layout, MenuText, _snippets.FetchFolderDetails());

        Log($"menu opened ({(historyOnly ? "history" : "main")}, {history.Count} clips)");

        HandleSelection(_menuRenderer.Show(menu, _window.Handle), target);
    }

    private void HandleSelection(MenuSelection selection, FocusToken target)
    {
        switch (selection)
        {
            case MenuSelection.Clip clip:
                PasteClip(clip.ClipId, target);
                break;

            case MenuSelection.SnippetChoice snippet:
                PasteSnippet(snippet.SnippetId, target);
                break;

            case MenuSelection.Command command:
                RunCommand(command.Kind);
                break;
        }
    }

    private void PasteSnippet(Guid snippetId, FocusToken target)
    {
        Snippet? snippet = _snippets.FetchSnippet(snippetId);

        if (snippet is null)
        {
            Log($"snippet {snippetId} vanished before it could be pasted");
            return;
        }

        // Snippets are plain text and are not history entries, so nothing is touched or deleted
        // here — the modifier actions do not apply. Port of the macOS snippet menu action.
        PasteResult result = _paste.PasteText(snippet.Content, target);
        Log($"snippet paste {result.Outcome} ('{snippet.Title}')");
    }

    /// <summary>Imports snippets through the real storage path, for the import diagnostic.</summary>
    public int ImportSnippets(string xml)
    {
        if (!SnippetXml.TryImport(xml, out IReadOnlyList<ImportedFolder> folders, out string? error))
        {
            throw new InvalidOperationException(error ?? "the file could not be read");
        }

        _snippets.InsertFolders(folders);
        return folders.Count;
    }

    /// <summary>Exports snippets from the real database, for the export diagnostic.</summary>
    public string ExportSnippets() => SnippetXml.Export(_snippets.FetchFolderDetails());

    /// <summary>Describes the stored snippet library, for the dump diagnostic.</summary>
    public void DumpSnippets(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        IReadOnlyList<SnippetFolderDetail> folders = _snippets.FetchFolderDetails();
        output.WriteLine($"{folders.Count} folders");

        foreach (SnippetFolderDetail detail in folders)
        {
            string state = detail.Folder.IsEnabled ? string.Empty : " (disabled)";
            output.WriteLine($"  [{detail.Folder.Index}] {detail.Folder.Title}{state}");

            foreach (Snippet snippet in detail.Snippets)
            {
                string snippetState = snippet.IsEnabled ? string.Empty : " (disabled)";
                output.WriteLine(
                    $"      [{snippet.Index}] {snippet.Title}{snippetState}  " +
                    $"{snippet.Content.Length} chars, {CountLines(snippet.Content)} lines");
                output.WriteLine($"          {Escape(snippet.Content)}");
            }
        }
    }

    private static int CountLines(string text) =>
        text.Length == 0 ? 0 : text.Split('\n').Length;

    /// <summary>Makes line breaks visible, so a lost CR shows up in the dump.</summary>
    private static string Escape(string text) =>
        text.Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    /// <summary>Builds the menu from the real database, for the --dump-menu diagnostic.</summary>
    public ClipMenu BuildMenuForInspection() =>
        MenuBuilder.BuildMainMenu(
            LoadHistory(),
            _settings.ToMenuSettings(),
            null,
            _snippets.FetchFolderDetails());

    private IReadOnlyList<HistoryClip> LoadHistory()
    {
        IReadOnlyList<ClipSummary> summaries =
            _store.FetchRecent(CurrentOrder, _settings.MaximumHistoryCount);

        bool wantsThumbnails = _settings.ShowsImages || _settings.ShowsColorPreviews;

        // One query for every preview rather than one per row, which is what keeps the menu snappy
        // with a full history.
        IReadOnlyDictionary<string, (string Kind, byte[] Data)> thumbnails = wantsThumbnails
            ? _store.FetchThumbnails(summaries.Select(summary => summary.Id).ToArray())
            : new Dictionary<string, (string, byte[])>();

        return summaries
            .Select(summary => new HistoryClip(
                summary.Id,
                summary.Title,
                summary.PrimaryFormat,
                ToThumbnail(thumbnails, summary.Id)))
            .ToArray();
    }

    private static ClipThumbnail? ToThumbnail(
        IReadOnlyDictionary<string, (string Kind, byte[] Data)> thumbnails,
        string clipId)
    {
        if (!thumbnails.TryGetValue(clipId, out (string Kind, byte[] Data) stored))
        {
            return null;
        }

        return Enum.TryParse(stored.Kind, out ClipThumbnailKind kind)
            ? new ClipThumbnail(kind, stored.Data)
            : null;
    }

    private void PasteClip(string clipId, FocusToken target)
    {
        ClipContent? content = _store.FetchContent(clipId);
        if (content is null)
        {
            Log($"clip {clipId[..12]} vanished before it could be pasted");
            return;
        }

        ClipSummary? summary = _store
            .FetchRecent(CurrentOrder, _settings.MaximumHistoryCount)
            .FirstOrDefault(clip => clip.Id == clipId);

        PasteResult result = _paste.Paste(new PasteRequest
        {
            ClipId = clipId,
            Content = content,
            IsConcealed = summary?.IsConcealed ?? false,
            Target = target,
        });

        Log($"paste {result.Outcome}" +
            (result.PastedAsPlainText ? " (plain text)" : string.Empty) +
            (result.ClipDeleted ? " (clip deleted)" : string.Empty));
    }

    private void RunCommand(MenuCommandKind kind)
    {
        switch (kind)
        {
            case MenuCommandKind.ClearHistory:
                ClearHistory();
                break;

            case MenuCommandKind.EditSnippets:
                ShowSnippetEditor();
                break;

            case MenuCommandKind.Settings:
                ShowSettings();
                break;

            case MenuCommandKind.CheckForUpdates:
                // Deliberately not awaited: the menu handler runs on the message-loop thread, and
                // blocking it would freeze the tray while a release host thinks about it.
                _ = CheckForUpdatesAsync(automatic: false);
                break;

            case MenuCommandKind.Quit:
                _requestQuit();
                break;
        }
    }

    /// <summary>
    /// Opens the snippet editor, or brings the open one forward.
    /// </summary>
    /// <remarks>
    /// A single instance, like the macOS shared window controller. It lives on the message-loop
    /// thread alongside everything else, so WPF and the raw Win32 message pump share one thread —
    /// which is why the window is shown non-modally rather than with <c>ShowDialog</c>: a modal
    /// loop would stop the clipboard monitor from seeing anything while the editor was open.
    /// </remarks>
    private void ShowSnippetEditor()
    {
        if (_snippetEditor is { } existing)
        {
            existing.Activate();
            return;
        }

        SnippetEditorViewModel model = new(_snippets);

        // Snippet changes affect the menu and the folder hotkeys, both rebuilt from the store on
        // demand, so there is nothing to invalidate — but a folder deleted here may still hold a
        // hotkey, and that is worth releasing.
        model.Changed += (_, _) => PruneSnippetFolderHotkeys();

        _snippetEditor = new SnippetEditorWindow(model);
        _snippetEditor.Closed += (_, _) => _snippetEditor = null;
        _snippetEditor.Show();
        _snippetEditor.Activate();

        Log("snippet editor opened");
    }

    /// <summary>
    /// Clears the history, asking first unless the user has turned the confirmation off.
    /// </summary>
    /// <remarks>
    /// Ticking "Don't ask again" turns <see cref="AppSettings.ShowsClearHistoryAlert"/> off, which
    /// is how macOS's suppression button behaves and the only way that setting normally changes.
    /// </remarks>
    private void ClearHistory()
    {
        if (_settings.ShowsClearHistoryAlert)
        {
            ClearHistoryDecision decision = ConfirmClearHistoryWindow.Ask(_localizer);

            if (decision.SuppressFutureAlerts)
            {
                ApplySettings(_settings with { ShowsClearHistoryAlert = false });
            }

            if (!decision.Confirmed)
            {
                return;
            }
        }

        _store.DeleteAll();

        // Reclaims the file space a history of screenshots can leave behind.
        _store.Vacuum();
        Log("history cleared");
    }

    /// <summary>Opens the settings window, or brings the open one forward.</summary>
    public void ShowSettings()
    {
        if (_settingsWindow is { } existing)
        {
            existing.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(() => _settings, ApplySettings, DescribeUpdateCheckAsync);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();

        Log("settings opened");
    }

    /// <summary>
    /// Takes a new settings object and makes every affected subsystem match it.
    /// </summary>
    /// <remarks>
    /// This is what "settings apply immediately" means in practice: the services each hold a
    /// settings snapshot, so every one of them has to be handed the new value. Hotkeys are the
    /// awkward case — they are a system-wide registration, so changing one means releasing the old
    /// combination before claiming the new one, which <c>HotkeyService.Bind</c> does.
    /// </remarks>
    private void ApplySettings(AppSettings updated)
    {
        ArgumentNullException.ThrowIfNull(updated);

        AppSettings previous = _settings;
        _settings = updated;

        try
        {
            updated.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log($"could not save settings: {exception.Message}");
        }

        _capture.Settings = updated.ToCaptureSettings();
        _paste.Settings = updated.ToPasteSettings();

        ApplyTrayIcon(previous, updated);
        ApplyHotkeys(previous, updated);
        ApplyHistoryClearTimer(updated);
        ApplyScreenshotWatcher(updated);
        ApplyLaunchAtLogin(previous, updated);

        // A smaller limit takes effect now rather than at the next copy.
        TrimHistory();
    }

    private void ApplyTrayIcon(AppSettings previous, AppSettings updated)
    {
        if (updated.HidesTrayIcon)
        {
            _trayIcon.Hide();
            return;
        }

        bool styleChanged = previous.TrayGlyphStyle != updated.TrayGlyphStyle;
        if (styleChanged || !_trayIcon.IsVisible)
        {
            _trayIcon.Show(TrayIconGlyph.Create(updated.TrayGlyphStyle));
        }
    }

    private void ApplyHotkeys(AppSettings previous, AppSettings updated)
    {
        IReadOnlyDictionary<string, KeyCombination?> before = previous.ToHotkeyBindings();
        IReadOnlyDictionary<string, KeyCombination?> after = updated.ToHotkeyBindings();

        foreach ((string actionId, KeyCombination? combination) in after)
        {
            // Rebinding an unchanged hotkey would briefly release a system-wide registration and
            // could lose it to another app in the gap, so only changes are applied.
            if (before.TryGetValue(actionId, out KeyCombination? existing) && existing == combination)
            {
                continue;
            }

            HotkeyResult result = _hotkeys.Bind(actionId, combination);
            Log(result.ToString());
        }
    }

    private void ApplyLaunchAtLogin(AppSettings previous, AppSettings updated)
    {
        if (previous.LaunchAtLogin == updated.LaunchAtLogin)
        {
            return;
        }

        if (LaunchAtLogin.Apply(LaunchAtLoginValueName, ExecutablePath, updated.LaunchAtLogin))
        {
            Log($"launch at login {(updated.LaunchAtLogin ? "enabled" : "disabled")}");
        }
        else
        {
            Log("could not change the launch-at-login registry entry");
        }
    }

    /// <summary>
    /// Starts, stops or re-times the periodic history wipe.
    /// </summary>
    /// <remarks>
    /// Port of the macOS <c>scheduleHistoryCleanup</c>. The timer fires on a thread-pool thread and
    /// only touches the database, which opens its own connection per operation — so it does not
    /// need to be marshalled onto the message-loop thread.
    /// </remarks>
    /// <summary>
    /// Starts the poll that decides when an automatic update check is due.
    /// </summary>
    /// <remarks>
    /// The timer ticks far more often than the check interval and asks
    /// <see cref="UpdateSchedule"/> each time, rather than being set to the interval itself. A
    /// laptop that was asleep when a check came due would otherwise wait a whole further interval
    /// after waking, and a settings change would need the timer rebuilt.
    /// </remarks>
    private void StartUpdateTimer()
    {
        if (!_updates.IsInstalled)
        {
            // A plain build or the portable zip. Nothing to update against, so do not spend a
            // timer or a request finding that out repeatedly.
            Log("updates: not an installed copy, automatic checks disabled");
            return;
        }

        _updateTimer = new Timer(
            _ => _ = CheckForUpdatesAsync(automatic: true),
            null,
            UpdateSchedule.StartupDelay,
            UpdateSchedule.PollInterval);

        Log($"updates: installed version {_updates.CurrentVersion}, "
            + $"checking every {UpdateSchedule.EffectiveInterval(_settings.ToUpdatePolicy()).TotalHours:0} hours");
    }

    /// <summary>
    /// Checks for a newer release, downloading one if it is there.
    /// </summary>
    /// <param name="automatic">
    /// True for the scheduled check, which respects the interval and the user's preference. False
    /// for the menu item, which the user asked for and so always runs.
    /// </param>
    /// <remarks>
    /// Runs off the message-loop thread. The only thing it touches on the way back is the settings
    /// file, and an update is staged rather than applied — see <see cref="UpdateService"/> for why
    /// a tray app must not restart itself underneath the user.
    /// </remarks>
    private async Task CheckForUpdatesAsync(bool automatic)
    {
        if (automatic
            && !UpdateSchedule.IsDue(_settings.ToUpdatePolicy(), _settings.LastUpdateCheckUtc, DateTimeOffset.UtcNow))
        {
            return;
        }

        try
        {
            UpdateCheck result = await _updates.CheckAsync().ConfigureAwait(false);

            Log(result.Outcome switch
            {
                UpdateOutcome.NotInstalled => "updates: not an installed copy",
                UpdateOutcome.UpToDate => $"updates: {result.Version} is current",
                UpdateOutcome.Ready => $"updates: {result.Message}",
                _ => $"updates: check failed: {result.Message}",
            });

            // Only a check that reached the feed resets the clock. Recording a failed one would
            // let a few minutes of no network suppress checking for a whole interval.
            if (result.Outcome is UpdateOutcome.UpToDate or UpdateOutcome.Ready)
            {
                RecordUpdateCheck();
            }
        }
        catch (Exception exception)
        {
            // Belt and braces: UpdateService already converts its failures into a result, so
            // reaching here means something genuinely unexpected, which still must not take down
            // a clipboard manager.
            Log($"updates: check failed unexpectedly: {exception.Message}");
        }
    }

    /// <summary>Runs a check for the settings pane and describes the result in one line.</summary>
    private async Task<string> DescribeUpdateCheckAsync()
    {
        UpdateCheck result = await _updates.CheckAsync().ConfigureAwait(true);

        if (result.Outcome is UpdateOutcome.UpToDate or UpdateOutcome.Ready)
        {
            RecordUpdateCheck();
        }

        return result.Outcome switch
        {
            UpdateOutcome.NotInstalled =>
                "This copy was not installed, so it cannot update itself.",
            UpdateOutcome.UpToDate => $"Up to date ({result.Version}).",
            UpdateOutcome.Ready =>
                $"Version {result.Version} downloaded. It applies the next time you reopen CopyPasta.",
            _ => $"Check failed: {result.Message}",
        };
    }

    /// <summary>Stamps the settings file with the time of a successful check.</summary>
    private void RecordUpdateCheck()
    {
        AppSettings stamped = _settings with { LastUpdateCheckUtc = DateTimeOffset.UtcNow };
        _settings = stamped;

        try
        {
            stamped.Save();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The schedule falls back to checking once per start, which is harmless.
            Log($"updates: could not record the check time: {exception.Message}");
        }
    }

    private void ApplyHistoryClearTimer(AppSettings settings)
    {
        _historyClearTimer?.Dispose();
        _historyClearTimer = null;

        if (!settings.ClearsHistoryPeriodically)
        {
            return;
        }

        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(settings.HistoryClearIntervalSeconds, 60));

        _historyClearTimer = new Timer(
            _ =>
            {
                try
                {
                    _store.DeleteAll();
                    Log("history cleared on schedule");
                }
                catch (Exception exception)
                {
                    Log($"scheduled history clear failed: {exception.Message}");
                }
            },
            null,
            interval,
            interval);

        Log($"history will be cleared every {interval.TotalMinutes:0} minutes");
    }

    /// <summary>
    /// Starts the background text recogniser, if the system has an OCR engine at all.
    /// </summary>
    private void StartTextRecognition()
    {
        WindowsTextRecognizer recognizer = new();

        if (!recognizer.IsAvailable)
        {
            // No OCR language pack installed. Say so once rather than failing per image.
            Log("text recognition unavailable: no OCR language pack is installed");
            return;
        }

        _ocr = new OcrQueue(recognizer, _store);
        _ocr.Trace += (_, message) => Log(message);

        Log($"text recognition ready ({recognizer.RecognizerLanguage})");

        if (_settings.RecognizesTextInImages)
        {
            // Pick up anything captured but never scanned, such as work a close interrupted.
            int queued = _ocr.EnqueueBacklog();
            if (queued > 0)
            {
                Log($"queued {queued} clips for text recognition");
            }
        }
    }

    /// <summary>Starts or stops watching the screenshots folder.</summary>
    private void ApplyScreenshotWatcher(AppSettings settings)
    {
        if (!settings.ObservesScreenshots)
        {
            _screenshots?.Dispose();
            _screenshots = null;
            return;
        }

        if (_screenshots is not null)
        {
            return;
        }

        _screenshots = new ScreenshotWatcher();
        _screenshots.ScreenshotCaptured += (_, path) => CaptureScreenshot(path);
        _screenshots.HandlerFailed += (_, exception) =>
            Log($"screenshot watcher failed: {exception.Message}");

        if (_screenshots.Start())
        {
            Log($"watching {_screenshots.Directory} for screenshots");
        }
        else
        {
            // Normal on a machine where no screenshot has ever been saved.
            Log($"not watching for screenshots: {_screenshots.Directory} does not exist");
            _screenshots.Dispose();
            _screenshots = null;
        }
    }

    private static string ExecutablePath =>
        Environment.ProcessPath ?? typeof(TrayApplication).Assembly.Location;

    private const string LaunchAtLoginValueName = "CopyPasta";

    /// <summary>
    /// Captures an image file, recognises its text, and reports what became searchable.
    /// </summary>
    /// <remarks>
    /// The Phase 7 acceptance check. Runs the real path — capture, recognise, index — rather than a
    /// parallel one, so a pass means the pipeline works and not merely that the OCR API returns
    /// something.
    /// </remarks>
    public async Task<int> RunOcrCheckAsync(string imagePath, TextWriter output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentNullException.ThrowIfNull(output);

        WindowsTextRecognizer recognizer = new();
        output.WriteLine($"OCR engine available : {recognizer.IsAvailable}");
        output.WriteLine($"Recognition language : {recognizer.RecognizerLanguage ?? "(none)"}");

        if (!recognizer.IsAvailable)
        {
            output.WriteLine("No OCR language pack is installed; nothing to check.");
            return 1;
        }

        byte[] bytes = await File.ReadAllBytesAsync(imagePath).ConfigureAwait(false);
        CaptureResult result = _capture.CaptureImage(ClipboardFormat.Png, bytes);

        if (!result.WasCaptured)
        {
            output.WriteLine($"FAIL  the image was not captured: {result.Outcome}");
            return 1;
        }

        string clipId = result.ClipId!;
        output.WriteLine($"Captured             : {clipId[..12]} ({bytes.Length:N0} bytes)");

        // The same post-capture work a real capture does — thumbnail, then both trims. Without it
        // this diagnostic would exercise only half the pipeline and report storage figures that no
        // real capture would ever produce.
        GenerateThumbnail(clipId);
        TrimHistory();

        await using OcrQueue queue = new(recognizer, _store);
        queue.Trace += (_, message) => output.WriteLine($"  {message}");
        queue.Enqueue(clipId);

        // Wait for the background queue to record a result.
        string? text = null;
        for (int attempt = 0; attempt < 60 && text is null; attempt++)
        {
            await Task.Delay(250).ConfigureAwait(false);
            text = FetchOcrText(clipId);
        }

        if (text is null)
        {
            output.WriteLine("FAIL  recognition did not complete within 15 seconds");
            return 1;
        }

        output.WriteLine($"Recognised text      : {text.Replace('\n', ' ').Trim()}");
        return 0;
    }

    /// <summary>Searches the history, for the search diagnostic.</summary>
    public void Search(string query, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(output);

        IReadOnlyList<ClipSummary> results = _store.Search(query, CurrentOrder, 50);
        output.WriteLine($"{results.Count} results for \"{query}\"");

        foreach (ClipSummary clip in results)
        {
            output.WriteLine($"  {clip.Id[..12]}  {clip.Title}");
        }
    }

    /// <summary>Reports the stored size, for the trimming diagnostic.</summary>
    public void ReportStorage(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        output.WriteLine(
            $"{_store.Count()} clips, {_store.TotalAssetBytes():N0} bytes, " +
            $"budget {_settings.MaximumHistoryMegabytes} MB, limit {_settings.MaximumHistoryCount} clips");
    }

    private string? FetchOcrText(string clipId) =>
        _store.FetchRecent(CurrentOrder, _settings.MaximumHistoryCount)
            .Any(clip => clip.Id == clipId)
            ? _store.FetchOcrText(clipId)
            : null;

    /// <summary>Renders every settings pane to PNGs, for layout verification.</summary>
    public void RenderSettingsPanes(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Directory.CreateDirectory(directory);

        SettingsWindow window = new(() => _settings, ApplySettings);
        window.Show();

        try
        {
            IReadOnlyList<string> names = window.PaneNames;

            for (int index = 0; index < names.Count; index++)
            {
                string safe = string.Concat(names[index].Split(Path.GetInvalidFileNameChars()));
                window.RenderPaneTo(Path.Combine(directory, $"{index:00}-{safe}.png"), index);
            }

            Log($"rendered {names.Count} settings panes to {directory}");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Releases hotkeys bound to folders that no longer exist.</summary>
    private void PruneSnippetFolderHotkeys()
    {
        HashSet<string> existing = _snippets
            .FetchFolderDetails()
            .Select(detail => detail.Folder.Id.ToString())
            .ToHashSet();

        foreach (string actionId in _hotkeys.LiveActions.ToArray())
        {
            if (Guid.TryParse(actionId, out _) && !existing.Contains(actionId))
            {
                _hotkeys.Unbind(actionId);
                Log($"released the hotkey for deleted snippet folder {actionId}");
            }
        }
    }

    private void Log(string message) => Trace?.Invoke(this, message);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _screenshots?.Dispose();
        _historyClearTimer?.Dispose();
        _updateTimer?.Dispose();

        // Recognition may be mid-image; give it a moment to unwind before the store goes away.
        _ocr?.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));

        // Hotkeys next: they are a system-wide registration, so releasing them before the window
        // they are bound to goes away keeps the shell's bookkeeping clean.
        _hotkeys.Dispose();
        _hotkeyRegistrar.Dispose();
        _trayIcon.Dispose();
        _monitor.Dispose();
        _menuRenderer.Dispose();
        _window.Dispose();
        _database.Dispose();
    }
}
