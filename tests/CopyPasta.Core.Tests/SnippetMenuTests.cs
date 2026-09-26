using CopyPasta.Core.Menu;
using CopyPasta.Core.Snippets;

namespace CopyPasta.Core.Tests;

/// <summary>The snippet portion of the menu. Port of macOS <c>addSnippetItems</c>.</summary>
public class SnippetMenuTests
{
    private static SnippetFolderDetail Folder(
        string title,
        bool isEnabled = true,
        params (string Title, string Content, bool IsEnabled)[] snippets)
    {
        Guid folderId = Guid.NewGuid();

        return new SnippetFolderDetail(
            new SnippetFolder(folderId, title, 0, isEnabled),
            snippets
                .Select((snippet, index) => new Snippet(
                    Guid.NewGuid(),
                    folderId,
                    snippet.Title,
                    snippet.Content,
                    index,
                    snippet.IsEnabled))
                .ToArray());
    }

    private static (string Title, string Content, bool IsEnabled) S(
        string title,
        string content = "body",
        bool isEnabled = true) => (title, content, isEnabled);

    private static IReadOnlyList<MenuNode> Section(
        IReadOnlyList<SnippetFolderDetail> folders,
        MenuLayoutSettings? settings = null,
        bool separate = false) =>
        MenuBuilder.BuildSnippetSection(folders, settings ?? MenuLayoutSettings.Default, separate);

    private static SnippetFolderMenuItem[] Folders(IReadOnlyList<MenuNode> nodes) =>
        nodes.OfType<SnippetFolderMenuItem>().ToArray();

    // ---- Structure ---------------------------------------------------------------------

    [Fact]
    public void Each_folder_becomes_a_submenu_of_its_snippets()
    {
        IReadOnlyList<MenuNode> nodes = Section(
        [
            Folder("Signatures", snippets: [S("Formal"), S("Casual")]),
            Folder("Boilerplate", snippets: [S("MIT")]),
        ]);

        SnippetFolderMenuItem[] folders = Folders(nodes);
        Assert.Equal(["Signatures", "Boilerplate"], folders.Select(folder => folder.Title));
        Assert.Equal(["1. Formal", "2. Casual"], folders[0].Items.Select(item => item.Title));
        Assert.Single(folders[1].Items);
    }

    [Fact]
    public void The_section_is_captioned()
    {
        IReadOnlyList<MenuNode> nodes = Section([Folder("F", snippets: [S("A")])]);

        Assert.Equal("Snippet", nodes.OfType<MenuLabel>().Single().Text);
    }

    [Fact]
    public void An_empty_library_contributes_nothing_at_all()
    {
        // Not even the caption, matching macOS — an empty "Snippet" heading would look broken.
        Assert.Empty(Section([]));
    }

    [Fact]
    public void A_library_of_only_disabled_folders_contributes_nothing()
    {
        Assert.Empty(Section([Folder("Off", isEnabled: false, snippets: [S("A")])]));
    }

    [Fact]
    public void The_combined_menu_separates_the_section_but_the_snippet_only_menu_does_not()
    {
        IReadOnlyList<SnippetFolderDetail> folders = [Folder("F", snippets: [S("A")])];

        Assert.IsType<MenuSeparator>(Section(folders, separate: true)[0]);
        Assert.IsType<MenuLabel>(Section(folders, separate: false)[0]);
    }

    // ---- Enabled state -------------------------------------------------------------------

    [Fact]
    public void A_disabled_folder_is_hidden_but_its_neighbours_are_not()
    {
        IReadOnlyList<MenuNode> nodes = Section(
        [
            Folder("Shown", snippets: [S("A")]),
            Folder("Hidden", isEnabled: false, snippets: [S("B")]),
        ]);

        Assert.Equal(["Shown"], Folders(nodes).Select(folder => folder.Title));
    }

    [Fact]
    public void A_disabled_snippet_is_hidden_but_its_folder_is_not()
    {
        IReadOnlyList<MenuNode> nodes = Section(
            [Folder("F", snippets: [S("Shown"), S("Hidden", isEnabled: false)])]);

        Assert.Equal(["1. Shown"], Folders(nodes)[0].Items.Select(item => item.Title));
    }

    [Fact]
    public void Numbering_skips_disabled_snippets_rather_than_leaving_gaps()
    {
        IReadOnlyList<MenuNode> nodes = Section(
            [Folder("F", snippets: [S("A"), S("skipped", isEnabled: false), S("C")])]);

        Assert.Equal([1, 2], Folders(nodes)[0].Items.Select(item => item.ListNumber));
    }

    [Fact]
    public void A_folder_whose_snippets_are_all_disabled_still_appears()
    {
        // The folder is enabled, so hiding it would be surprising; it simply has nothing in it.
        IReadOnlyList<MenuNode> nodes = Section(
            [Folder("F", snippets: [S("A", isEnabled: false)])]);

        Assert.Empty(Assert.Single(Folders(nodes)).Items);
    }

    // ---- Titles and tooltips -----------------------------------------------------------------

    [Fact]
    public void Numbering_follows_the_same_settings_as_the_history()
    {
        IReadOnlyList<MenuNode> nodes = Section(
            [Folder("F", snippets: [S("A"), S("B")])],
            MenuLayoutSettings.Default with { StartsNumberingAtZero = true });

        Assert.Equal(["0. A", "1. B"], Folders(nodes)[0].Items.Select(item => item.Title));
    }

    [Fact]
    public void Numbering_can_be_turned_off()
    {
        IReadOnlyList<MenuNode> nodes = Section(
            [Folder("F", snippets: [S("A")])],
            MenuLayoutSettings.Default with { ShowsNumbers = false });

        Assert.Equal("A", Folders(nodes)[0].Items[0].Title);
    }

    [Fact]
    public void Long_titles_are_trimmed()
    {
        IReadOnlyList<MenuNode> nodes = Section(
            [Folder(new string('f', 50), snippets: [S(new string('s', 50))])],
            MenuLayoutSettings.Default with { MaximumTitleLength = 10, ShowsNumbers = false });

        Assert.Equal(10, Folders(nodes)[0].Title.Length);
        Assert.Equal(10, Folders(nodes)[0].Items[0].Title.Length);
    }

    [Fact]
    public void The_tooltip_shows_the_body_not_the_title()
    {
        // The title is what you can already read; the body is what you are about to paste.
        IReadOnlyList<MenuNode> nodes = Section(
            [Folder("F", snippets: [S("Formal", "Kind regards,\nAlex")])]);

        Assert.Equal("Kind regards,\nAlex", Folders(nodes)[0].Items[0].ToolTip);
    }

    [Fact]
    public void Tooltips_can_be_turned_off()
    {
        IReadOnlyList<MenuNode> nodes = Section(
            [Folder("F", snippets: [S("A")])],
            MenuLayoutSettings.Default with { ShowsToolTips = false });

        Assert.Null(Folders(nodes)[0].Items[0].ToolTip);
    }

    // ---- The folder menu the per-folder hotkey pops ---------------------------------------------

    [Fact]
    public void A_folder_menu_is_captioned_with_the_folder_and_lists_its_snippets()
    {
        ClipMenu menu = MenuBuilder.BuildSnippetFolderMenu(
            Folder("Signatures", snippets: [S("Formal"), S("Casual")]),
            MenuLayoutSettings.Default);

        Assert.Equal("Signatures", menu.Nodes.OfType<MenuLabel>().Single().Text);
        Assert.Equal(["1. Formal", "2. Casual"], menu.AllSnippetItems.Select(item => item.Title));
    }

    [Fact]
    public void A_folder_menu_hides_disabled_snippets()
    {
        ClipMenu menu = MenuBuilder.BuildSnippetFolderMenu(
            Folder("F", snippets: [S("Shown"), S("Hidden", isEnabled: false)]),
            MenuLayoutSettings.Default);

        Assert.Single(menu.AllSnippetItems);
    }

    // ---- The combined menu -----------------------------------------------------------------------

    [Fact]
    public void The_main_menu_puts_snippets_between_the_history_and_the_commands()
    {
        ClipMenu menu = MenuBuilder.BuildMainMenu(
            [new HistoryClip("clip1", "text")],
            MenuLayoutSettings.Default,
            snippetFolders: [Folder("F", snippets: [S("A")])]);

        List<string> order = menu.Nodes
            .Select(node => node switch
            {
                MenuLabel label => label.Text,
                ClipFolderMenuItem => "history",
                SnippetFolderMenuItem => "snippets",
                MenuCommand command => command.Kind.ToString(),
                _ => "-",
            })
            .ToList();

        Assert.True(
            order.IndexOf("history") < order.IndexOf("snippets"),
            "history should come before snippets");
        Assert.True(
            order.IndexOf("snippets") < order.IndexOf(nameof(MenuCommandKind.Quit)),
            "snippets should come before the commands");
    }

    [Fact]
    public void The_main_menu_omits_the_snippet_section_when_there_are_none()
    {
        ClipMenu menu = MenuBuilder.BuildMainMenu(
            [new HistoryClip("clip1", "text")],
            MenuLayoutSettings.Default);

        Assert.Empty(menu.Nodes.OfType<SnippetFolderMenuItem>());
        Assert.DoesNotContain("Snippet", menu.Nodes.OfType<MenuLabel>().Select(label => label.Text));
    }

    [Fact]
    public void The_snippet_only_menu_has_no_history_and_no_commands()
    {
        ClipMenu menu = MenuBuilder.BuildSnippetMenu(
            [Folder("F", snippets: [S("A")])],
            MenuLayoutSettings.Default);

        Assert.Empty(menu.AllClipItems);
        Assert.Empty(menu.Nodes.OfType<MenuCommand>());
        Assert.Single(menu.AllSnippetItems);
    }

    [Fact]
    public void All_snippet_items_walks_every_folder()
    {
        ClipMenu menu = MenuBuilder.BuildSnippetMenu(
        [
            Folder("One", snippets: [S("A"), S("B")]),
            Folder("Two", snippets: [S("C")]),
        ],
            MenuLayoutSettings.Default);

        Assert.Equal(3, menu.AllSnippetItems.Count());
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            MenuBuilder.BuildSnippetSection(null!, MenuLayoutSettings.Default, false));
        Assert.Throws<ArgumentNullException>(() =>
            MenuBuilder.BuildSnippetSection([], null!, false));
        Assert.Throws<ArgumentNullException>(() =>
            MenuBuilder.BuildSnippetFolderMenu(null!, MenuLayoutSettings.Default));
    }
}
