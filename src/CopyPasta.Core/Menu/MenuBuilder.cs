namespace CopyPasta.Core.Menu;

using CopyPasta.Core.Clipboard;
using CopyPasta.Core.Snippets;

/// <summary>A history entry as the menu builder needs it.</summary>
/// <param name="Id">The clip to paste when chosen.</param>
/// <param name="Title">The clip's text, untrimmed — the builder does the trimming.</param>
/// <param name="PrimaryFormat">Highest-priority stored format, for the kind prefix and icon.</param>
/// <param name="Thumbnail">A preview image, when one was generated.</param>
public sealed record HistoryClip(
    string Id,
    string Title,
    ClipboardFormat? PrimaryFormat = null,
    ClipThumbnail? Thumbnail = null);

/// <summary>Menu text, gathered in one place so it can be localised.</summary>
public sealed record MenuStrings
{
    public static MenuStrings Default { get; } = new();

    public string History { get; init; } = "History";

    public string Snippets { get; init; } = "Snippet";

    public string ClearHistory { get; init; } = "Clear History";

    public string EditSnippets { get; init; } = "Edit Snippets";

    public string Settings { get; init; } = "Settings…";

    public string CheckForUpdates { get; init; } = "Check for Updates…";

    public string Quit { get; init; } = "Quit";
}

/// <summary>
/// Turns stored clips into a menu shape. Port of macOS
/// <c>MenuManager.addHistoryItems</c> / <c>createClipMenu</c>.
/// </summary>
/// <remarks>
/// <para>
/// The macOS original does this in one pass with four interleaved counters (<c>i</c>,
/// <c>listNumber</c>, <c>subMenuCount</c>, <c>subMenuIndex</c>) and looks submenus back up by
/// their position in the parent menu. It works, but the invariants are implicit and a change to
/// any one counter breaks the others silently.
/// </para>
/// <para>
/// This computes the same shape as an explicit chunking pass: take the inline run, then slice the
/// remainder into folders. Every folder's numbering restarts, and its title spans the global
/// positions it covers — both matching macOS.
/// </para>
/// </remarks>
public static class MenuBuilder
{
    private const int MaximumAcceleratedItems = 10;

    /// <summary>The history portion: a caption, then inline entries, then folders.</summary>
    public static IReadOnlyList<MenuNode> BuildHistorySection(
        IReadOnlyList<HistoryClip> clips,
        MenuLayoutSettings settings,
        MenuStrings? strings = null)
    {
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(settings);
        strings ??= MenuStrings.Default;

        List<MenuNode> nodes = [new MenuLabel(strings.History)];

        int shown = Math.Min(clips.Count, Math.Max(settings.MaximumHistoryCount, 0));
        if (shown == 0)
        {
            return nodes;
        }

        int firstNumber = settings.StartsNumberingAtZero ? 0 : 1;
        int inlineCount = Math.Min(Math.Max(settings.InlineItemLimit, 0), shown);

        for (int index = 0; index < inlineCount; index++)
        {
            nodes.Add(BuildItem(clips[index], index, firstNumber + index, settings));
        }

        // A non-positive folder size would make the macOS loop stall; clamp instead.
        int folderSize = Math.Max(settings.FolderItemLimit, 1);

        for (int offset = inlineCount; offset < shown; offset += folderSize)
        {
            int take = Math.Min(folderSize, shown - offset);

            List<ClipMenuItem> items = new(take);
            for (int position = 0; position < take; position++)
            {
                int index = offset + position;
                items.Add(BuildItem(clips[index], index, firstNumber + position, settings));
            }

            // The range covers global positions. macOS clamps the upper bound to the number of
            // clips rather than to the last clip's number, which is off by one for a partial
            // final folder when numbering starts at zero ("20 - 25" for items 20..24). Clamping
            // to the last actual position is correct and matches macOS everywhere else.
            string title = ClipTitleFormatter.Range(
                firstNumber + offset,
                firstNumber + offset + take - 1);

            nodes.Add(new ClipFolderMenuItem(title, items, IconFor(MenuIcon.Folder, settings)));
        }

        return nodes;
    }

    /// <summary>
    /// The snippet portion: a separator, a caption, then one submenu per enabled folder.
    /// Port of macOS <c>addSnippetItems</c>.
    /// </summary>
    /// <param name="separate">
    /// Prefix a separator, as the combined menu does. The snippet-only menu does not.
    /// </param>
    /// <remarks>
    /// Disabled folders and disabled snippets are filtered here rather than in the query, so the
    /// editor can still show and toggle them. An empty library contributes nothing at all — not
    /// even the caption — matching macOS.
    /// </remarks>
    public static IReadOnlyList<MenuNode> BuildSnippetSection(
        IReadOnlyList<SnippetFolderDetail> folders,
        MenuLayoutSettings settings,
        bool separate,
        MenuStrings? strings = null)
    {
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(settings);
        strings ??= MenuStrings.Default;

        SnippetFolderDetail[] enabled = folders
            .Where(detail => detail.Folder.IsEnabled)
            .ToArray();

        if (enabled.Length == 0)
        {
            return [];
        }

        List<MenuNode> nodes = [];

        if (separate)
        {
            nodes.Add(new MenuSeparator());
        }

        nodes.Add(new MenuLabel(strings.Snippets));

        foreach (SnippetFolderDetail detail in enabled)
        {
            nodes.Add(new SnippetFolderMenuItem(
                detail.Folder.Id,
                ClipTitleFormatter.TrimForMenu(detail.Folder.Title, settings.MaximumTitleLength),
                BuildSnippetItems(detail, settings),
                IconFor(MenuIcon.Folder, settings)));
        }

        return nodes;
    }

    /// <summary>
    /// One folder's snippets, as the per-folder hotkey pops them.
    /// Port of macOS <c>popUpSnippetFolder</c>.
    /// </summary>
    public static ClipMenu BuildSnippetFolderMenu(
        SnippetFolderDetail folder,
        MenuLayoutSettings settings,
        MenuStrings? strings = null)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(settings);

        List<MenuNode> nodes =
        [
            new MenuLabel(ClipTitleFormatter.TrimForMenu(folder.Folder.Title, settings.MaximumTitleLength)),
            .. BuildSnippetItems(folder, settings),
        ];

        return new ClipMenu(nodes);
    }

    /// <summary>
    /// The glyph for a node, or none when the user has menu icons turned off.
    /// </summary>
    /// <remarks>
    /// Decided here rather than in the renderer so the choice is part of the model and testable
    /// without a display, like every other layout decision.
    /// </remarks>
    private static MenuIcon IconFor(MenuIcon icon, MenuLayoutSettings settings) =>
        settings.ShowsIcons ? icon : MenuIcon.None;

    private static IReadOnlyList<SnippetMenuItem> BuildSnippetItems(
        SnippetFolderDetail detail,
        MenuLayoutSettings settings)
    {
        int listNumber = settings.StartsNumberingAtZero ? 0 : 1;
        List<SnippetMenuItem> items = [];

        foreach (Snippet snippet in detail.EnabledSnippets)
        {
            string title = ClipTitleFormatter.TrimForMenu(snippet.Title, settings.MaximumTitleLength);

            items.Add(new SnippetMenuItem(
                snippet.Id,
                ClipTitleFormatter.Numbered(title, listNumber, settings.ShowsNumbers),

                // Built from the snippet's body, not its title: the title is what you can already
                // read, the body is what you are about to paste.
                ClipTitleFormatter.ToolTip(
                    snippet.Content,
                    settings.ShowsToolTips,
                    settings.MaximumToolTipLength),
                listNumber,
                IconFor(MenuIcon.Snippet, settings)));

            listNumber++;
        }

        return items;
    }

    /// <summary>The whole tray menu: history, then snippets, then the commands.</summary>
    public static ClipMenu BuildMainMenu(
        IReadOnlyList<HistoryClip> clips,
        MenuLayoutSettings settings,
        MenuStrings? strings = null,
        IReadOnlyList<SnippetFolderDetail>? snippetFolders = null)
    {
        ArgumentNullException.ThrowIfNull(clips);
        ArgumentNullException.ThrowIfNull(settings);
        strings ??= MenuStrings.Default;

        List<MenuNode> nodes = [.. BuildHistorySection(clips, settings, strings)];

        if (snippetFolders is { Count: > 0 })
        {
            nodes.AddRange(BuildSnippetSection(snippetFolders, settings, separate: true, strings));
        }

        nodes.Add(new MenuSeparator());

        if (settings.ShowsClearHistoryCommand)
        {
            // Disabled with an empty history, matching the macOS validateMenuItem behaviour.
            nodes.Add(new MenuCommand(
                MenuCommandKind.ClearHistory,
                strings.ClearHistory,
                IsEnabled: clips.Count > 0));
        }

        nodes.Add(new MenuCommand(MenuCommandKind.EditSnippets, strings.EditSnippets));
        nodes.Add(new MenuCommand(MenuCommandKind.Settings, strings.Settings, Accelerator: ","));
        nodes.Add(new MenuCommand(MenuCommandKind.CheckForUpdates, strings.CheckForUpdates));
        nodes.Add(new MenuSeparator());
        nodes.Add(new MenuCommand(MenuCommandKind.Quit, strings.Quit));

        return new ClipMenu(nodes);
    }

    /// <summary>A history-only menu, for the separate history hotkey.</summary>
    public static ClipMenu BuildHistoryMenu(
        IReadOnlyList<HistoryClip> clips,
        MenuLayoutSettings settings,
        MenuStrings? strings = null) =>
        new(BuildHistorySection(clips, settings, strings));

    /// <summary>A snippet-only menu, for the separate snippet hotkey.</summary>
    public static ClipMenu BuildSnippetMenu(
        IReadOnlyList<SnippetFolderDetail> folders,
        MenuLayoutSettings settings,
        MenuStrings? strings = null) =>
        new(BuildSnippetSection(folders, settings, separate: false, strings));

    private static ClipMenuItem BuildItem(
        HistoryClip clip,
        int globalIndex,
        int listNumber,
        MenuLayoutSettings settings)
    {
        string typed = ClipTitleFormatter.TypedTitle(
            clip.Title,
            clip.PrimaryFormat,
            settings.MaximumTitleLength);

        return new ClipMenuItem(
            clip.Id,
            ClipTitleFormatter.Numbered(typed, listNumber, settings.ShowsNumbers),
            ClipTitleFormatter.ToolTip(clip.Title, settings.ShowsToolTips, settings.MaximumToolTipLength),
            listNumber,
            AcceleratorFor(globalIndex, settings),
            clip.PrimaryFormat,
            SelectThumbnail(clip.Thumbnail, settings));
    }

    /// <summary>
    /// The digit for an entry, or null. Keyed on the entry's global position, so only the first ten
    /// entries get one however the menu is chunked — matching macOS.
    /// </summary>
    private static string? AcceleratorFor(int globalIndex, MenuLayoutSettings settings)
    {
        if (!settings.AddsNumericAccelerators || globalIndex >= MaximumAcceleratedItems)
        {
            return null;
        }

        int number = settings.StartsNumberingAtZero ? globalIndex : globalIndex + 1;

        // Ten has no single digit, so the tenth entry gets 0 — which also makes the run read
        // 1…9, 0 across the top of a keyboard.
        return number == MaximumAcceleratedItems ? "0" : number.ToString();
    }

    /// <summary>
    /// Honours the two preview settings separately.
    /// </summary>
    /// <remarks>
    /// macOS decides which thumbnail kind to store at capture time, and its
    /// <c>thumbnailAsset</c> builder overwrites an image thumbnail with a colour swatch
    /// unconditionally — so a colour code wins even for a user who turned colour previews off and
    /// image previews on. Filtering at display time instead means each setting does what it says.
    /// </remarks>
    private static ClipThumbnail? SelectThumbnail(ClipThumbnail? thumbnail, MenuLayoutSettings settings)
    {
        if (thumbnail is null)
        {
            return null;
        }

        return thumbnail.Kind switch
        {
            ClipThumbnailKind.Image => settings.ShowsImages ? thumbnail : null,
            ClipThumbnailKind.ColorCode => settings.ShowsColorPreviews ? thumbnail : null,
            _ => null,
        };
    }
}
