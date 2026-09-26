using CopyPasta.Core.Menu;

namespace CopyPasta.Core.Tests;

/// <summary>
/// The menu shape, against the structure the macOS <c>MenuManager</c> produces. The inline limits
/// of 0, 10 and 30 are the Phase 3 acceptance criterion; the traces they are checked against were
/// worked out by hand from the original's four interleaved counters.
/// </summary>
public class MenuBuilderTests
{
    private static IReadOnlyList<HistoryClip> Clips(int count) =>
        Enumerable.Range(0, count)
            .Select(index => new HistoryClip($"clip{index}", $"text {index}"))
            .ToArray();

    private static IReadOnlyList<MenuNode> History(
        int clipCount,
        MenuLayoutSettings? settings = null) =>
        MenuBuilder.BuildHistorySection(
            Clips(clipCount),
            settings ?? MenuLayoutSettings.Default);

    /// <summary>The clip entries that sit directly in the menu.</summary>
    private static ClipMenuItem[] InlineItems(IReadOnlyList<MenuNode> nodes) =>
        nodes.OfType<ClipMenuItem>().ToArray();

    private static ClipFolderMenuItem[] Folders(IReadOnlyList<MenuNode> nodes) =>
        nodes.OfType<ClipFolderMenuItem>().ToArray();

    // ---- Acceptance: the three inline limits ------------------------------------------

    [Fact]
    public void Inline_limit_of_zero_puts_every_entry_in_folders_of_ten()
    {
        // macOS default. 30 clips become three folders titled 1 - 10, 11 - 20, 21 - 30, each
        // numbering its own contents from 1.
        IReadOnlyList<MenuNode> nodes = History(30);

        Assert.Empty(InlineItems(nodes));

        ClipFolderMenuItem[] folders = Folders(nodes);
        Assert.Equal(3, folders.Length);
        Assert.Equal(["1 - 10", "11 - 20", "21 - 30"], folders.Select(folder => folder.Title));
        Assert.All(folders, folder => Assert.Equal(10, folder.Items.Count));
        Assert.Equal(
            Enumerable.Range(1, 10),
            folders[1].Items.Select(item => item.ListNumber));
    }

    [Fact]
    public void Inline_limit_of_ten_shows_ten_entries_then_two_folders()
    {
        IReadOnlyList<MenuNode> nodes = History(
            30,
            MenuLayoutSettings.Default with { InlineItemLimit = 10 });

        ClipMenuItem[] inline = InlineItems(nodes);
        Assert.Equal(10, inline.Length);
        Assert.Equal(Enumerable.Range(1, 10), inline.Select(item => item.ListNumber));

        ClipFolderMenuItem[] folders = Folders(nodes);
        Assert.Equal(["11 - 20", "21 - 30"], folders.Select(folder => folder.Title));
        Assert.All(folders, folder => Assert.Equal(10, folder.Items.Count));
    }

    [Fact]
    public void Inline_limit_of_thirty_shows_everything_inline_with_no_folders()
    {
        IReadOnlyList<MenuNode> nodes = History(
            30,
            MenuLayoutSettings.Default with { InlineItemLimit = 30 });

        Assert.Equal(30, InlineItems(nodes).Length);
        Assert.Empty(Folders(nodes));
        Assert.Equal(30, InlineItems(nodes)[^1].ListNumber);
    }

    // ---- Chunking edges -----------------------------------------------------------------

    [Fact]
    public void A_partial_final_folder_holds_only_what_is_left()
    {
        IReadOnlyList<MenuNode> nodes = History(25);

        ClipFolderMenuItem[] folders = Folders(nodes);
        Assert.Equal(["1 - 10", "11 - 20", "21 - 25"], folders.Select(folder => folder.Title));
        Assert.Equal(5, folders[^1].Items.Count);
    }

    [Fact]
    public void An_inline_limit_that_is_not_a_multiple_of_the_folder_size_still_lines_up()
    {
        // Traced against the macOS counters: 5 inline, then folders 6 - 15, 16 - 25, 26 - 30.
        IReadOnlyList<MenuNode> nodes = History(
            30,
            MenuLayoutSettings.Default with { InlineItemLimit = 5 });

        Assert.Equal(5, InlineItems(nodes).Length);
        Assert.Equal(
            ["6 - 15", "16 - 25", "26 - 30"],
            Folders(nodes).Select(folder => folder.Title));
        Assert.Equal(5, Folders(nodes)[^1].Items.Count);
    }

    [Fact]
    public void An_inline_limit_beyond_the_clip_count_shows_them_all_inline()
    {
        IReadOnlyList<MenuNode> nodes = History(
            4,
            MenuLayoutSettings.Default with { InlineItemLimit = 30 });

        Assert.Equal(4, InlineItems(nodes).Length);
        Assert.Empty(Folders(nodes));
    }

    [Fact]
    public void A_single_clip_gets_a_one_entry_folder()
    {
        IReadOnlyList<MenuNode> nodes = History(1);

        ClipFolderMenuItem folder = Assert.Single(Folders(nodes));
        Assert.Equal("1 - 1", folder.Title);
        Assert.Single(folder.Items);
    }

    [Fact]
    public void An_empty_history_is_just_the_caption()
    {
        IReadOnlyList<MenuNode> nodes = History(0);

        MenuLabel label = Assert.IsType<MenuLabel>(Assert.Single(nodes));
        Assert.Equal("History", label.Text);
    }

    [Fact]
    public void The_history_limit_caps_what_is_shown()
    {
        IReadOnlyList<MenuNode> nodes = History(
            100,
            MenuLayoutSettings.Default with { MaximumHistoryCount = 12 });

        Assert.Equal(
            ["1 - 10", "11 - 12"],
            Folders(nodes).Select(folder => folder.Title));
    }

    [Fact]
    public void A_zero_history_limit_shows_nothing()
    {
        Assert.Single(History(30, MenuLayoutSettings.Default with { MaximumHistoryCount = 0 }));
    }

    [Fact]
    public void A_non_positive_folder_size_does_not_stall()
    {
        // The macOS loop makes no progress with a folder size of zero. Clamped to one here.
        IReadOnlyList<MenuNode> nodes = History(
            3,
            MenuLayoutSettings.Default with { FolderItemLimit = 0 });

        Assert.Equal(3, Folders(nodes).Length);
        Assert.All(Folders(nodes), folder => Assert.Single(folder.Items));
    }

    // ---- Numbering from zero ---------------------------------------------------------------

    [Fact]
    public void Numbering_from_zero_shifts_titles_and_folder_ranges()
    {
        IReadOnlyList<MenuNode> nodes = History(
            30,
            MenuLayoutSettings.Default with { StartsNumberingAtZero = true });

        Assert.Equal(
            ["0 - 9", "10 - 19", "20 - 29"],
            Folders(nodes).Select(folder => folder.Title));
        Assert.Equal(
            Enumerable.Range(0, 10),
            Folders(nodes)[0].Items.Select(item => item.ListNumber));
    }

    [Fact]
    public void A_partial_final_folder_numbered_from_zero_ends_on_the_last_real_entry()
    {
        // macOS reports "20 - 25" here because it clamps to the clip count rather than the last
        // position; with 25 clips numbered from zero the last one is 24.
        IReadOnlyList<MenuNode> nodes = History(
            25,
            MenuLayoutSettings.Default with { StartsNumberingAtZero = true });

        Assert.Equal("20 - 24", Folders(nodes)[^1].Title);
    }

    // ---- Titles -----------------------------------------------------------------------------

    [Fact]
    public void Titles_are_numbered_by_default()
    {
        ClipMenuItem item = Folders(History(1))[0].Items[0];

        Assert.Equal("1. text 0", item.Title);
    }

    [Fact]
    public void Numbering_can_be_turned_off()
    {
        IReadOnlyList<MenuNode> nodes = History(
            1,
            MenuLayoutSettings.Default with { ShowsNumbers = false });

        Assert.Equal("text 0", Folders(nodes)[0].Items[0].Title);
    }

    [Fact]
    public void A_clip_carries_its_id_through_to_the_menu_item()
    {
        Assert.Equal("clip0", Folders(History(1))[0].Items[0].ClipId);
    }

    // ---- Numeric accelerators ----------------------------------------------------------------

    [Fact]
    public void Accelerators_are_off_by_default()
    {
        Assert.All(
            Folders(History(5))[0].Items,
            item => Assert.Null(item.Accelerator));
    }

    [Fact]
    public void Only_the_first_ten_entries_get_an_accelerator_however_the_menu_is_chunked()
    {
        IReadOnlyList<MenuNode> nodes = History(
            30,
            MenuLayoutSettings.Default with { AddsNumericAccelerators = true });

        ClipFolderMenuItem[] folders = Folders(nodes);

        // The tenth entry gets 0, so the run reads 1…9, 0.
        Assert.Equal(
            ["1", "2", "3", "4", "5", "6", "7", "8", "9", "0"],
            folders[0].Items.Select(item => item.Accelerator));

        // Keyed on global position, so the second folder gets none even though its entries are
        // numbered 1 - 10 again.
        Assert.All(folders[1].Items, item => Assert.Null(item.Accelerator));
    }

    [Fact]
    public void Accelerators_numbered_from_zero_run_zero_to_nine()
    {
        IReadOnlyList<MenuNode> nodes = History(
            12,
            MenuLayoutSettings.Default with
            {
                AddsNumericAccelerators = true,
                StartsNumberingAtZero = true,
            });

        Assert.Equal(
            ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9"],
            Folders(nodes)[0].Items.Select(item => item.Accelerator));
    }

    [Fact]
    public void Accelerators_span_the_inline_run_and_the_first_folder()
    {
        IReadOnlyList<MenuNode> nodes = History(
            20,
            MenuLayoutSettings.Default with
            {
                InlineItemLimit = 4,
                AddsNumericAccelerators = true,
            });

        Assert.Equal(
            ["1", "2", "3", "4"],
            InlineItems(nodes).Select(item => item.Accelerator));
        Assert.Equal(
            ["5", "6", "7", "8", "9", "0", null, null, null, null],
            Folders(nodes)[0].Items.Select(item => item.Accelerator));
    }

    // ---- Thumbnails --------------------------------------------------------------------------

    [Fact]
    public void An_image_thumbnail_is_dropped_when_image_previews_are_off()
    {
        HistoryClip clip = new(
            "a",
            "pic",
            ClipboardFormat.Png,
            new ClipThumbnail(ClipThumbnailKind.Image, [1]));

        IReadOnlyList<MenuNode> nodes = MenuBuilder.BuildHistorySection(
            [clip],
            MenuLayoutSettings.Default with { ShowsImages = false });

        Assert.Null(Folders(nodes)[0].Items[0].Thumbnail);
    }

    [Fact]
    public void A_colour_swatch_survives_when_only_image_previews_are_off()
    {
        // The macOS thumbnail builder cannot express this: it overwrites an image thumbnail with a
        // colour swatch unconditionally, so the two settings are not independent there.
        HistoryClip clip = new(
            "a",
            "#3366ff",
            ClipboardFormat.UnicodeText,
            new ClipThumbnail(ClipThumbnailKind.ColorCode, [1]));

        IReadOnlyList<MenuNode> nodes = MenuBuilder.BuildHistorySection(
            [clip],
            MenuLayoutSettings.Default with { ShowsImages = false, ShowsColorPreviews = true });

        Assert.NotNull(Folders(nodes)[0].Items[0].Thumbnail);
    }

    // ---- The whole menu -----------------------------------------------------------------------

    [Fact]
    public void The_main_menu_ends_with_the_command_block()
    {
        ClipMenu menu = MenuBuilder.BuildMainMenu(Clips(3), MenuLayoutSettings.Default);

        Assert.Equal(
            [
                MenuCommandKind.ClearHistory,
                MenuCommandKind.EditSnippets,
                MenuCommandKind.Settings,
                MenuCommandKind.CheckForUpdates,
                MenuCommandKind.Quit,
            ],
            menu.Nodes.OfType<MenuCommand>().Select(command => command.Kind));
    }

    [Fact]
    public void Clear_history_is_disabled_with_an_empty_history()
    {
        ClipMenu menu = MenuBuilder.BuildMainMenu([], MenuLayoutSettings.Default);

        MenuCommand clear = menu.Nodes
            .OfType<MenuCommand>()
            .Single(command => command.Kind == MenuCommandKind.ClearHistory);
        Assert.False(clear.IsEnabled);
    }

    [Fact]
    public void Clear_history_can_be_hidden_entirely()
    {
        ClipMenu menu = MenuBuilder.BuildMainMenu(
            Clips(3),
            MenuLayoutSettings.Default with { ShowsClearHistoryCommand = false });

        Assert.DoesNotContain(
            MenuCommandKind.ClearHistory,
            menu.Nodes.OfType<MenuCommand>().Select(command => command.Kind));
    }

    [Fact]
    public void All_clip_items_walks_inline_entries_and_folders_alike()
    {
        ClipMenu menu = MenuBuilder.BuildMainMenu(
            Clips(25),
            MenuLayoutSettings.Default with { InlineItemLimit = 5 });

        Assert.Equal(25, menu.AllClipItems.Count());
    }

    [Fact]
    public void The_history_only_menu_has_no_commands()
    {
        ClipMenu menu = MenuBuilder.BuildHistoryMenu(Clips(3), MenuLayoutSettings.Default);

        Assert.Empty(menu.Nodes.OfType<MenuCommand>());
        Assert.Equal(3, menu.AllClipItems.Count());
    }

    [Fact]
    public void Menu_text_can_be_localised()
    {
        ClipMenu menu = MenuBuilder.BuildMainMenu(
            Clips(1),
            MenuLayoutSettings.Default,
            new MenuStrings { History = "Verlauf", Quit = "Beenden" });

        Assert.Equal("Verlauf", menu.Nodes.OfType<MenuLabel>().First().Text);
        Assert.Equal(
            "Beenden",
            menu.Nodes.OfType<MenuCommand>().Single(c => c.Kind == MenuCommandKind.Quit).Title);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MenuBuilder.BuildHistorySection(null!, MenuLayoutSettings.Default));
        Assert.Throws<ArgumentNullException>(() =>
            MenuBuilder.BuildHistorySection([], null!));
        Assert.Throws<ArgumentNullException>(() =>
            MenuBuilder.BuildMainMenu(null!, MenuLayoutSettings.Default));
    }
}
