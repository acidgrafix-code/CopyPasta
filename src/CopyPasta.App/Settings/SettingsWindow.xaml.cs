using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CopyPasta.Core.Clipboard;
using CopyPasta.Core.Hotkeys;
using CopyPasta.Core.Paste;
using CopyPasta.Core.Updates;
using CopyPasta.Interop;
using Microsoft.Win32;

namespace CopyPasta.App.Settings;

/// <summary>A content type with its checkbox state, for the Type pane.</summary>
public sealed class ContentTypeToggle : INotifyPropertyChanged
{
    private bool _isEnabled;

    public required ClipContentType Type { get; init; }

    public required string Label { get; init; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
            {
                return;
            }

            _isEnabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEnabled)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>An omitted or pending setting, for the Beta pane.</summary>
public sealed record CoverageEntry(string Title, string Note);

/// <summary>
/// The settings window. Port of the eight macOS settings panes.
/// </summary>
/// <remarks>
/// Every control writes straight back through <see cref="Apply"/>, which hands a whole new
/// <see cref="AppSettings"/> to the app and saves it. There is no OK/Cancel: macOS settings panes
/// apply immediately and so do these, which also means the app never holds a half-edited copy.
/// </remarks>
public partial class SettingsWindow : Window
{
    private static readonly (int Seconds, string Label)[] ClearIntervals =
    [
        (300, "Every 5 minutes"),
        (1800, "Every 30 minutes"),
        (3600, "Every hour"),
        (14400, "Every 4 hours"),
        (28800, "Every 8 hours"),
        (86400, "Every day"),
        (259200, "Every 3 days"),
        (604800, "Every 7 days"),
    ];

    /// <summary>
    /// Update-check intervals. The macOS app offers daily, weekly and monthly; hourly is added
    /// here because it is the floor <see cref="UpdateSchedule"/> enforces anyway.
    /// </summary>
    private static readonly (int Seconds, string Label)[] UpdateIntervals =
    [
        (3600, "Every hour"),
        (86400, "Every day"),
        (604800, "Every week"),
        (2592000, "Every 30 days"),
    ];

    private static readonly ModifierKey[] Modifiers =
    [
        ModifierKey.Shift,
        ModifierKey.Control,
        ModifierKey.Alt,
        ModifierKey.Windows,
    ];

    private readonly Func<AppSettings> _read;
    private readonly Action<AppSettings> _write;

    /// <summary>
    /// Runs a check on demand and reports what happened, or null when the host supplies none.
    /// </summary>
    /// <remarks>
    /// A delegate rather than a reference to the update service, so the window stays constructible
    /// from a test or the pane-rendering diagnostic without one.
    /// </remarks>
    private readonly Func<Task<string>>? _checkForUpdates;
    private readonly ObservableCollection<ContentTypeToggle> _contentTypes = [];
    private readonly ObservableCollection<string> _excludedApps = [];

    /// <summary>Suppresses write-back while the controls are being populated.</summary>
    private bool _loading;

    public SettingsWindow(
        Func<AppSettings> read,
        Action<AppSettings> write,
        Func<Task<string>>? checkForUpdates = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);

        _read = read;
        _write = write;
        _checkForUpdates = checkForUpdates;

        InitializeComponent();
        PopulateFixedLists();
        Load();
    }

    /// <summary>
    /// Renders a pane to a PNG, for verifying the layout without a human.
    /// </summary>
    /// <remarks>
    /// <c>PrintWindow</c> comes back blank for WPF content, which composes through DirectX rather
    /// than drawing into the window's DC. Rendering the visual tree directly is the only way to see
    /// what a pane actually looks like from a script.
    /// </remarks>
    public void RenderPaneTo(string path, int tabIndex)
    {
        Tabs.SelectedIndex = Math.Clamp(tabIndex, 0, Tabs.Items.Count - 1);

        // Let the newly selected tab lay out before it is rendered.
        UpdateLayout();
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

        int width = (int)Math.Max(ActualWidth, 1);
        int height = (int)Math.Max(ActualHeight, 1);

        RenderTargetBitmap target = new(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(this);

        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(target));

        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Tab headers, so a diagnostic can name what it captured.</summary>
    public IReadOnlyList<string> PaneNames => Tabs.Items
        .OfType<TabItem>()
        .Select(tab => tab.Header?.ToString() ?? string.Empty)
        .ToArray();

    // ---- Populating ---------------------------------------------------------------------

    private void PopulateFixedLists()
    {
        foreach ((int _, string label) in ClearIntervals)
        {
            ClearInterval.Items.Add(new ComboBoxItem { Content = label });
        }

        foreach ((int _, string label) in UpdateIntervals)
        {
            UpdateCheckInterval.Items.Add(new ComboBoxItem { Content = label });
        }

        foreach (ComboBox box in new[] { PlainTextModifier, DeleteModifier, PasteAndDeleteModifier })
        {
            foreach (ModifierKey modifier in Modifiers)
            {
                box.Items.Add(new ComboBoxItem { Content = Describe(modifier) });
            }
        }

        foreach (ClipContentType type in ClipContentTypes.All)
        {
            _contentTypes.Add(new ContentTypeToggle { Type = type, Label = Describe(type) });
        }

        ContentTypes.ItemsSource = _contentTypes;
        ExcludedApps.ItemsSource = _excludedApps;

        CoverageSummary.Text = SettingsCoverage.Summary();

        // Availability is a property of the machine, not a setting, so say so plainly rather than
        // leaving a checkbox that quietly does nothing.
        WindowsTextRecognizer recognizer = new();
        OcrStatus.Text = recognizer.IsAvailable
            ? $"Recognition uses the OCR engine built into Windows ({recognizer.RecognizerLanguage})."
            : "No OCR language pack is installed, so text in images cannot be read on this machine.";
        AdditionsList.ItemsSource = SettingsCoverage.Additions
            .Select(setting => new CoverageEntry(setting.WindowsProperty ?? "(unnamed)", setting.Note ?? string.Empty))
            .ToArray();

        CoverageList.ItemsSource = SettingsCoverage.NotPorted
            .Select(setting => new CoverageEntry(
                $"{setting.MacOsName} — {setting.Status.ToString().ToLowerInvariant()}",
                setting.Note ?? string.Empty))
            .ToArray();

        VersionText.Text =
            $"Version {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";

        LocaleText.Text = $"Interface language: {CultureInfo.CurrentUICulture.Name}";
        PathsText.Text =
            $"Settings: {AppSettings.DefaultPath}{Environment.NewLine}" +
            $"Data: {Path.Combine(AppSettings.DefaultDirectory, "clips.db")}";
    }

    private void Load()
    {
        _loading = true;

        try
        {
            AppSettings settings = _read();

            // General
            LaunchAtLogin.IsChecked = settings.LaunchAtLogin;
            PastesAutomatically.IsChecked = settings.PastesAutomatically;
            MaximumHistoryCount.Text = settings.MaximumHistoryCount.ToString(CultureInfo.InvariantCulture);
            SortOrder.SelectedIndex = settings.ReordersClipsAfterPasting ? 1 : 0;
            ClearsHistoryOnQuit.IsChecked = settings.ClearsHistoryOnQuit;
            ClearsHistoryPeriodically.IsChecked = settings.ClearsHistoryPeriodically;
            ClearInterval.SelectedIndex = Math.Max(
                0,
                Array.FindIndex(ClearIntervals, entry => entry.Seconds == settings.HistoryClearIntervalSeconds));
            TrayStyle.SelectedIndex = settings.HidesTrayIcon
                ? 2
                : settings.TrayGlyphStyle == TrayGlyphStyle.Light ? 0 : 1;

            // Updates
            AutomaticallyChecksForUpdates.IsChecked = settings.AutomaticallyChecksForUpdates;
            UpdateCheckInterval.SelectedIndex = Math.Max(
                0,
                Array.FindIndex(UpdateIntervals, entry => entry.Seconds == settings.UpdateCheckIntervalSeconds));
            UpdateStatusText.Text = settings.LastUpdateCheckUtc is { } checkedAt
                ? $"Last checked {checkedAt.ToLocalTime():f}"
                : "Never checked";
            CheckNowButton.IsEnabled = _checkForUpdates is not null;

            // Menu
            ShowsNumbers.IsChecked = settings.ShowsNumbers;
            StartsNumberingAtZero.IsChecked = settings.StartsNumberingAtZero;
            AddsNumericAccelerators.IsChecked = settings.AddsNumericAccelerators;
            ShowsToolTips.IsChecked = settings.ShowsToolTips;
            ShowsIconsInMenu.IsChecked = settings.ShowsIconsInMenu;
            ShowsImages.IsChecked = settings.ShowsImages;
            ShowsColorPreviews.IsChecked = settings.ShowsColorPreviews;
            ShowsClearHistoryCommand.IsChecked = settings.ShowsClearHistoryCommand;
            ShowsClearHistoryAlert.IsChecked = settings.ShowsClearHistoryAlert;
            InlineItemLimit.Text = settings.InlineItemLimit.ToString(CultureInfo.InvariantCulture);
            FolderItemLimit.Text = settings.FolderItemLimit.ToString(CultureInfo.InvariantCulture);
            MaximumTitleLength.Text = settings.MaximumTitleLength.ToString(CultureInfo.InvariantCulture);
            MaximumToolTipLength.Text = settings.MaximumToolTipLength.ToString(CultureInfo.InvariantCulture);
            ThumbnailWidth.Text = settings.ThumbnailWidth.ToString(CultureInfo.InvariantCulture);
            ThumbnailHeight.Text = settings.ThumbnailHeight.ToString(CultureInfo.InvariantCulture);

            // Type
            foreach (ContentTypeToggle toggle in _contentTypes)
            {
                toggle.IsEnabled = !settings.DisabledContentTypes.Contains(
                    toggle.Type.ToSettingsKey(),
                    StringComparer.OrdinalIgnoreCase);
            }

            ObservesScreenshots.IsChecked = settings.ObservesScreenshots;
            RecognizesTextInImages.IsChecked = settings.RecognizesTextInImages;
            MaximumHistoryMegabytes.Text =
                settings.MaximumHistoryMegabytes.ToString(CultureInfo.InvariantCulture);
            IgnoresConcealedContent.IsChecked = settings.IgnoresConcealedContent;
            IgnoresCloudClipboard.IsChecked = settings.IgnoresCloudClipboard;
            AllowsDuplicates.IsChecked = settings.AllowsDuplicates;
            OverwritesDuplicates.IsChecked = settings.OverwritesDuplicates;

            // Exclude
            _excludedApps.Clear();
            foreach (string app in settings.ExcludedApplications)
            {
                _excludedApps.Add(app);
            }

            // Shortcuts
            MainMenuHotkey.Combination = Parse(settings.MainMenuHotkey);
            HistoryMenuHotkey.Combination = Parse(settings.HistoryMenuHotkey);
            SnippetMenuHotkey.Combination = Parse(settings.SnippetMenuHotkey);
            EditSnippetsHotkey.Combination = Parse(settings.EditSnippetsHotkey);
            ClearHistoryHotkey.Combination = Parse(settings.ClearHistoryHotkey);

            PastesPlainTextWithModifier.IsChecked = settings.PastesPlainTextWithModifier;
            PlainTextModifier.SelectedIndex = IndexOf(settings.PlainTextModifier);
            DeletesWithModifier.IsChecked = settings.DeletesWithModifier;
            DeleteModifier.SelectedIndex = IndexOf(settings.DeleteModifier);
            PastesAndDeletesWithModifier.IsChecked = settings.PastesAndDeletesWithModifier;
            PasteAndDeleteModifier.SelectedIndex = IndexOf(settings.PasteAndDeleteModifier);
        }
        finally
        {
            _loading = false;
        }

        // Wired after loading so populating the recorders does not write back.
        foreach (HotkeyRecorder recorder in new[]
                 {
                     MainMenuHotkey, HistoryMenuHotkey, SnippetMenuHotkey,
                     EditSnippetsHotkey, ClearHistoryHotkey,
                 })
        {
            recorder.CombinationChanged -= OnHotkeyChanged;
            recorder.CombinationChanged += OnHotkeyChanged;
        }
    }

    // ---- Writing back ---------------------------------------------------------------------

    private void OnChanged(object sender, RoutedEventArgs e) => Apply();

    /// <summary>
    /// Runs a check the user asked for, regardless of the schedule.
    /// </summary>
    /// <remarks>
    /// The button is disabled for the duration rather than the window being blocked: a release
    /// host can take a while, and a settings window that stops repainting looks like a hang.
    /// </remarks>
    private async void OnCheckForUpdates(object sender, RoutedEventArgs e)
    {
        if (_checkForUpdates is null)
        {
            return;
        }

        CheckNowButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking…";

        try
        {
            UpdateStatusText.Text = await _checkForUpdates().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            // An async void handler that throws takes the process down, so nothing may escape.
            UpdateStatusText.Text = $"Check failed: {exception.Message}";
        }
        finally
        {
            CheckNowButton.IsEnabled = true;
        }
    }

    private void OnHotkeyChanged(object? sender, KeyCombination? combination) => Apply();

    private void Apply()
    {
        if (_loading)
        {
            return;
        }

        AppSettings current = _read();

        AppSettings updated = current with
        {
            // General
            LaunchAtLogin = LaunchAtLogin.IsChecked == true,
            PastesAutomatically = PastesAutomatically.IsChecked == true,
            MaximumHistoryCount = Number(MaximumHistoryCount, current.MaximumHistoryCount, 1, 1000),
            ReordersClipsAfterPasting = SortOrder.SelectedIndex == 1,
            ClearsHistoryOnQuit = ClearsHistoryOnQuit.IsChecked == true,
            ClearsHistoryPeriodically = ClearsHistoryPeriodically.IsChecked == true,
            HistoryClearIntervalSeconds = ClearIntervals[
                Math.Clamp(ClearInterval.SelectedIndex, 0, ClearIntervals.Length - 1)].Seconds,

            // Updates
            AutomaticallyChecksForUpdates = AutomaticallyChecksForUpdates.IsChecked == true,
            UpdateCheckIntervalSeconds = UpdateIntervals[
                Math.Clamp(UpdateCheckInterval.SelectedIndex, 0, UpdateIntervals.Length - 1)].Seconds,
            HidesTrayIcon = TrayStyle.SelectedIndex == 2,
            TrayGlyphStyle = TrayStyle.SelectedIndex == 1 ? TrayGlyphStyle.Dark : TrayGlyphStyle.Light,

            // Menu
            ShowsNumbers = ShowsNumbers.IsChecked == true,
            StartsNumberingAtZero = StartsNumberingAtZero.IsChecked == true,
            AddsNumericAccelerators = AddsNumericAccelerators.IsChecked == true,
            ShowsToolTips = ShowsToolTips.IsChecked == true,
            ShowsIconsInMenu = ShowsIconsInMenu.IsChecked == true,
            ShowsImages = ShowsImages.IsChecked == true,
            ShowsColorPreviews = ShowsColorPreviews.IsChecked == true,
            ShowsClearHistoryCommand = ShowsClearHistoryCommand.IsChecked == true,
            ShowsClearHistoryAlert = ShowsClearHistoryAlert.IsChecked == true,
            InlineItemLimit = Number(InlineItemLimit, current.InlineItemLimit, 0, 1000),
            FolderItemLimit = Number(FolderItemLimit, current.FolderItemLimit, 1, 1000),
            MaximumTitleLength = Number(MaximumTitleLength, current.MaximumTitleLength, 3, 500),
            MaximumToolTipLength = Number(MaximumToolTipLength, current.MaximumToolTipLength, 0, 10000),
            ThumbnailWidth = Number(ThumbnailWidth, current.ThumbnailWidth, 8, 400),
            ThumbnailHeight = Number(ThumbnailHeight, current.ThumbnailHeight, 8, 400),

            // Type
            DisabledContentTypes = _contentTypes
                .Where(toggle => !toggle.IsEnabled)
                .Select(toggle => toggle.Type.ToSettingsKey())
                .ToList(),
            ObservesScreenshots = ObservesScreenshots.IsChecked == true,
            RecognizesTextInImages = RecognizesTextInImages.IsChecked == true,
            MaximumHistoryMegabytes = Number(
                MaximumHistoryMegabytes, current.MaximumHistoryMegabytes, 0, 100_000),
            IgnoresConcealedContent = IgnoresConcealedContent.IsChecked == true,
            IgnoresCloudClipboard = IgnoresCloudClipboard.IsChecked == true,
            AllowsDuplicates = AllowsDuplicates.IsChecked == true,
            OverwritesDuplicates = OverwritesDuplicates.IsChecked == true,

            // Exclude
            ExcludedApplications = _excludedApps.ToList(),

            // Shortcuts
            MainMenuHotkey = Format(MainMenuHotkey.Combination),
            HistoryMenuHotkey = Format(HistoryMenuHotkey.Combination),
            SnippetMenuHotkey = Format(SnippetMenuHotkey.Combination),
            EditSnippetsHotkey = Format(EditSnippetsHotkey.Combination),
            ClearHistoryHotkey = Format(ClearHistoryHotkey.Combination),

            PastesPlainTextWithModifier = PastesPlainTextWithModifier.IsChecked == true,
            PlainTextModifier = ModifierAt(PlainTextModifier, current.PlainTextModifier),
            DeletesWithModifier = DeletesWithModifier.IsChecked == true,
            DeleteModifier = ModifierAt(DeleteModifier, current.DeleteModifier),
            PastesAndDeletesWithModifier = PastesAndDeletesWithModifier.IsChecked == true,
            PasteAndDeleteModifier = ModifierAt(PasteAndDeleteModifier, current.PasteAndDeleteModifier),
        };

        _write(updated);

        // Re-show the numbers that were clamped, so a rejected value does not linger on screen.
        MaximumHistoryCount.Text = updated.MaximumHistoryCount.ToString(CultureInfo.InvariantCulture);
        Status("Saved.");
    }

    // ---- Excluded applications ----------------------------------------------------------------

    private void OnAddExcludedApp(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Choose an application to exclude",
            Filter = "Applications (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        // Stored as the executable name rather than the full path. macOS excludes by bundle id,
        // which survives the app moving; the process name is the closest Windows equivalent, and
        // ExcludedApplicationMatcher accepts either form.
        string name = Path.GetFileNameWithoutExtension(dialog.FileName);

        if (!_excludedApps.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _excludedApps.Add(name);
            Apply();
        }
    }

    private void OnRemoveExcludedApp(object sender, RoutedEventArgs e)
    {
        if (ExcludedApps.SelectedItem is string selected)
        {
            _excludedApps.Remove(selected);
            Apply();
        }
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    private void Status(string message) => StatusText.Text = message;

    private static KeyCombination? Parse(string? text) =>
        KeyCombination.TryParse(text, out KeyCombination combination) ? combination : null;

    private static string? Format(KeyCombination? combination) =>
        combination is { } value && value.IsValid ? value.ToString() : null;

    private static int IndexOf(ModifierKey modifier) => Math.Max(0, Array.IndexOf(Modifiers, modifier));

    private static ModifierKey ModifierAt(ComboBox box, ModifierKey fallback) =>
        box.SelectedIndex >= 0 && box.SelectedIndex < Modifiers.Length
            ? Modifiers[box.SelectedIndex]
            : fallback;

    /// <summary>
    /// A number from a text box, clamped, falling back to the current value when it is not a number
    /// at all — so a half-typed entry cannot wipe a setting.
    /// </summary>
    private static int Number(TextBox box, int fallback, int minimum, int maximum) =>
        int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? Math.Clamp(value, minimum, maximum)
            : fallback;

    private static string Describe(ModifierKey modifier) => modifier switch
    {
        ModifierKey.Shift => "Shift",
        ModifierKey.Control => "Ctrl",
        ModifierKey.Alt => "Alt",
        ModifierKey.Windows => "Win",
        _ => modifier.ToString(),
    };

    private static string Describe(ClipContentType type) => type switch
    {
        ClipContentType.Text => "Plain text",
        ClipContentType.Rtf => "Rich text (RTF)",
        ClipContentType.Html => "HTML",
        ClipContentType.Pdf => "PDF",
        ClipContentType.Files => "Files",
        ClipContentType.Url => "URLs",
        ClipContentType.Image => "Images",
        _ => type.ToString(),
    };
}
