namespace CopyPasta.Core.Menu;

using CopyPasta.Core.Clipboard;

/// <summary>One entry in the tray menu.</summary>
/// <remarks>
/// A platform-independent description of the menu, deliberately separate from any Win32 menu
/// handle. macOS builds its <c>NSMenu</c> in a single pass that indexes back into the menu by
/// position; that is the hardest code in the original to follow and the easiest to break. Here the
/// shape is computed first, as data, and rendering is a separate mechanical walk.
///
/// Titles are unescaped. The renderer is responsible for platform quoting — on Win32 an
/// ampersand in menu text marks a keyboard accelerator and has to be doubled.
/// </remarks>
public abstract record MenuNode;

/// <summary>A disabled caption, such as "History".</summary>
public sealed record MenuLabel(string Text) : MenuNode;

public sealed record MenuSeparator : MenuNode;

/// <summary>A history entry.</summary>
/// <param name="ClipId">Identifies the clip to paste.</param>
/// <param name="Title">Display text, already numbered and trimmed.</param>
/// <param name="ToolTip">Longer preview, or null when tooltips are off.</param>
/// <param name="ListNumber">The number shown in the title; restarts inside each folder.</param>
/// <param name="Accelerator">
/// A single digit the user can press, or null. Only the first ten entries overall get one.
/// </param>
/// <param name="PrimaryFormat">The clip's primary format, for choosing an icon.</param>
/// <param name="Thumbnail">Image bytes to show beside the item, when previews are on.</param>
public sealed record ClipMenuItem(
    string ClipId,
    string Title,
    string? ToolTip,
    int ListNumber,
    string? Accelerator,
    ClipboardFormat? PrimaryFormat,
    ClipThumbnail? Thumbnail = null) : MenuNode;

/// <summary>A small glyph shown beside a menu entry.</summary>
/// <remarks>
/// Port of the macOS <c>showsIconsInMenu</c> artwork: a folder glyph on submenus and a text glyph
/// on snippets. Clips get a thumbnail instead, so they never carry one.
/// </remarks>
public enum MenuIcon
{
    None,
    Folder,
    Snippet,
}

/// <summary>A submenu holding a run of history entries.</summary>
/// <param name="Title">A range such as "11 - 20".</param>
public sealed record ClipFolderMenuItem(
    string Title,
    IReadOnlyList<ClipMenuItem> Items,
    MenuIcon Icon = MenuIcon.None) : MenuNode;

/// <summary>A snippet the user can paste.</summary>
/// <param name="SnippetId">Identifies the snippet whose content gets pasted.</param>
public sealed record SnippetMenuItem(
    Guid SnippetId,
    string Title,
    string? ToolTip,
    int ListNumber,
    MenuIcon Icon = MenuIcon.None) : MenuNode;

/// <summary>A submenu holding one snippet folder's snippets.</summary>
public sealed record SnippetFolderMenuItem(
    Guid FolderId,
    string Title,
    IReadOnlyList<SnippetMenuItem> Items,
    MenuIcon Icon = MenuIcon.None) : MenuNode;

/// <summary>A preview image for a menu item.</summary>
/// <param name="Kind">What the image represents, so settings can enable them separately.</param>
/// <param name="Data">Encoded image bytes.</param>
public sealed record ClipThumbnail(ClipThumbnailKind Kind, byte[] Data);

public enum ClipThumbnailKind
{
    /// <summary>A scaled-down copy of a copied image.</summary>
    Image,

    /// <summary>A swatch of a copied colour code such as <c>#3366ff</c>.</summary>
    ColorCode,
}

/// <summary>An action the menu can invoke.</summary>
public enum MenuCommandKind
{
    ClearHistory,
    EditSnippets,
    Settings,
    CheckForUpdates,
    Quit,
}

/// <param name="Title">Display text.</param>
/// <param name="Accelerator">Accelerator character, or null.</param>
/// <param name="IsEnabled">Greyed out when false.</param>
public sealed record MenuCommand(
    MenuCommandKind Kind,
    string Title,
    string? Accelerator = null,
    bool IsEnabled = true) : MenuNode;

/// <summary>A complete menu, ready to render.</summary>
public sealed record ClipMenu(IReadOnlyList<MenuNode> Nodes)
{
    public static ClipMenu Empty { get; } = new([]);

    /// <summary>Every clip entry, inline ones and those inside folders alike.</summary>
    public IEnumerable<ClipMenuItem> AllClipItems =>
        Nodes.SelectMany(node => node switch
        {
            ClipMenuItem item => [item],
            ClipFolderMenuItem folder => folder.Items,
            _ => Enumerable.Empty<ClipMenuItem>(),
        });

    /// <summary>Every snippet entry, across all folders.</summary>
    public IEnumerable<SnippetMenuItem> AllSnippetItems =>
        Nodes.SelectMany(node => node switch
        {
            SnippetMenuItem item => [item],
            SnippetFolderMenuItem folder => folder.Items,
            _ => Enumerable.Empty<SnippetMenuItem>(),
        });
}

/// <summary>
/// Layout settings for the menu. Port of the <c>@Shared</c> values read by the macOS
/// <c>MenuManager</c>.
/// </summary>
public sealed record MenuLayoutSettings
{
    public static MenuLayoutSettings Default { get; } = new();

    /// <summary>
    /// How many entries appear directly in the menu before folders start. Zero — the macOS
    /// default — means every entry lives in a folder. Port of <c>inlineMenuItemLimit</c>.
    /// </summary>
    public int InlineItemLimit { get; init; }

    /// <summary>Entries per folder. Port of <c>folderMenuItemLimit</c>.</summary>
    public int FolderItemLimit { get; init; } = 10;

    /// <summary>Entries to show at all. Port of <c>maximumHistoryCount</c>.</summary>
    public int MaximumHistoryCount { get; init; } = 30;

    /// <summary>Number entries from 0 rather than 1. Port of <c>startsMenuItemTitlesAtZero</c>.</summary>
    public bool StartsNumberingAtZero { get; init; }

    /// <summary>Prefix entries with "N. ". Port of <c>marksMenuItemsWithNumbers</c>.</summary>
    public bool ShowsNumbers { get; init; } = true;

    /// <summary>
    /// Give the first ten entries a digit accelerator. Port of <c>addsNumericKeyEquivalents</c>.
    /// </summary>
    public bool AddsNumericAccelerators { get; init; }

    /// <summary>Characters of title to show. Port of <c>maximumMenuItemTitleLength</c>.</summary>
    public int MaximumTitleLength { get; init; } = 20;

    /// <summary>Port of <c>showsToolTipsOnMenuItems</c>.</summary>
    public bool ShowsToolTips { get; init; } = true;

    /// <summary>Port of <c>maximumToolTipLength</c>.</summary>
    public int MaximumToolTipLength { get; init; } = 200;

    /// <summary>Show folder and text icons. Port of <c>showsIconsInMenu</c>.</summary>
    public bool ShowsIcons { get; init; } = true;

    /// <summary>Show image thumbnails. Port of <c>showsImagesInMenu</c>.</summary>
    public bool ShowsImages { get; init; } = true;

    /// <summary>Show colour swatches. Port of <c>showsColorPreviewInMenu</c>.</summary>
    public bool ShowsColorPreviews { get; init; } = true;

    /// <summary>Offer "Clear History". Port of <c>showsClearHistoryMenuItem</c>.</summary>
    public bool ShowsClearHistoryCommand { get; init; } = true;
}
