using CopyPasta.App.Snippets;
using CopyPasta.Core.Snippets;
using CopyPasta.Data;

namespace CopyPasta.App.Tests;

/// <summary>
/// The snippet editor's behaviour, against a real database but without a window.
/// </summary>
/// <remarks>
/// Closes a gap called out at the end of Phase 5: the editor's operations had only been compiled,
/// not exercised. The view model holds everything that matters, so testing it covers the editing
/// rules even though the WPF surface still needs a human.
/// </remarks>
public sealed class SnippetEditorViewModelTests : IDisposable
{
    private readonly ClipDatabase _database;
    private readonly SqliteSnippetStore _store;
    private readonly SnippetEditorViewModel _model;

    public SnippetEditorViewModelTests()
    {
        _database = ClipDatabase.InMemory($"editor-{Guid.NewGuid():n}");
        _database.Migrate();
        _store = new SqliteSnippetStore(_database);
        _model = new SnippetEditorViewModel(_store);
    }

    public void Dispose() => _database.Dispose();

    private FolderNode FolderWith(params string[] titles)
    {
        FolderNode folder = _model.AddFolder();

        foreach (string title in titles)
        {
            SnippetNode snippet = _model.AddSnippet(folder)!;
            _model.Rename(snippet, title);
        }

        return folder;
    }

    /// <summary>Re-reads from the database, to prove a change was actually written through.</summary>
    private IReadOnlyList<SnippetFolderDetail> Stored() => _store.FetchFolderDetails();

    // ---- Creating -----------------------------------------------------------------------

    [Fact]
    public void A_new_folder_appears_in_the_tree_and_in_the_database()
    {
        FolderNode folder = _model.AddFolder();

        Assert.Same(folder, Assert.Single(_model.Folders));
        Assert.Equal(folder.Id, Assert.Single(Stored()).Folder.Id);
    }

    [Fact]
    public void A_snippet_is_added_to_the_selected_folder()
    {
        FolderNode folder = _model.AddFolder();

        SnippetNode? snippet = _model.AddSnippet(folder);

        Assert.NotNull(snippet);
        Assert.Same(snippet, Assert.Single(folder.Snippets));
    }

    [Fact]
    public void A_snippet_added_while_a_sibling_is_selected_joins_the_same_folder()
    {
        FolderNode folder = FolderWith("First");

        SnippetNode? second = _model.AddSnippet(folder.Snippets[0]);

        Assert.Equal(2, folder.Snippets.Count);
        Assert.Equal(folder.Id, second!.FolderId);
    }

    [Fact]
    public void Adding_a_snippet_with_no_folders_at_all_does_nothing()
    {
        Assert.Null(_model.AddSnippet(null));
    }

    [Fact]
    public void A_snippet_added_with_nothing_selected_goes_to_the_last_folder()
    {
        _model.AddFolder();
        FolderNode last = _model.AddFolder();

        SnippetNode? snippet = _model.AddSnippet(null);

        Assert.Equal(last.Id, snippet!.FolderId);
    }

    // ---- Editing -------------------------------------------------------------------------

    [Fact]
    public void Renaming_writes_through()
    {
        FolderNode folder = _model.AddFolder();

        _model.Rename(folder, "Signatures");

        Assert.Equal("Signatures", Assert.Single(Stored()).Folder.Title);
    }

    [Fact]
    public void An_empty_title_falls_back_to_the_default_rather_than_a_blank_menu_row()
    {
        FolderNode folder = _model.AddFolder();
        SnippetNode snippet = _model.AddSnippet(folder)!;

        _model.Rename(folder, "   ");
        _model.Rename(snippet, string.Empty);

        Assert.Equal("untitled folder", folder.Title);
        Assert.Equal("untitled snippet", snippet.Title);
    }

    [Fact]
    public void Content_writes_through_including_line_breaks()
    {
        FolderNode folder = _model.AddFolder();
        SnippetNode snippet = _model.AddSnippet(folder)!;

        _model.UpdateContent(snippet, "line one\r\n    indented");

        Assert.Equal("line one\r\n    indented", _store.FetchSnippet(snippet.Id)!.Content);
    }

    [Fact]
    public void Toggling_enabled_writes_through_both_ways()
    {
        FolderNode folder = _model.AddFolder();

        _model.ToggleEnabled(folder);
        Assert.False(Assert.Single(Stored()).Folder.IsEnabled);

        _model.ToggleEnabled(folder);
        Assert.True(Assert.Single(Stored()).Folder.IsEnabled);
    }

    // ---- Deleting --------------------------------------------------------------------------

    [Fact]
    public void Deleting_a_folder_removes_it_and_its_snippets()
    {
        FolderNode folder = FolderWith("A", "B");

        _model.Delete(folder);

        Assert.Empty(_model.Folders);
        Assert.Empty(Stored());
    }

    [Fact]
    public void Deleting_a_snippet_leaves_its_folder_alone()
    {
        FolderNode folder = FolderWith("A", "B");

        _model.Delete(folder.Snippets[0]);

        Assert.Single(folder.Snippets);
        Assert.Single(Assert.Single(Stored()).Snippets);
    }

    // ---- Reordering --------------------------------------------------------------------------

    [Fact]
    public void Folders_can_be_reordered_and_the_order_is_written_through()
    {
        FolderNode first = _model.AddFolder();
        _model.Rename(first, "First");
        FolderNode second = _model.AddFolder();
        _model.Rename(second, "Second");

        _model.MoveFolder(second, 0);

        Assert.Equal(["Second", "First"], _model.Folders.Select(folder => folder.Title));
        Assert.Equal(["Second", "First"], Stored().Select(detail => detail.Folder.Title));
    }

    [Fact]
    public void Moving_a_folder_to_where_it_already_is_changes_nothing()
    {
        FolderNode folder = _model.AddFolder();

        _model.MoveFolder(folder, 0);

        Assert.Same(folder, Assert.Single(_model.Folders));
    }

    [Fact]
    public void A_target_index_beyond_the_list_is_clamped()
    {
        FolderNode first = _model.AddFolder();
        _model.AddFolder();

        _model.MoveFolder(first, 99);

        Assert.Equal(1, _model.Folders.IndexOf(first));
    }

    [Fact]
    public void Snippets_can_be_reordered_within_a_folder()
    {
        FolderNode folder = FolderWith("A", "B", "C");
        SnippetNode last = folder.Snippets[2];

        _model.MoveSnippet(last, folder, 0);

        Assert.Equal(["C", "A", "B"], folder.Snippets.Select(snippet => snippet.Title));
        Assert.Equal(
            ["C", "A", "B"],
            Stored()[0].Snippets.Select(snippet => snippet.Title));
    }

    // ---- Moving between folders ------------------------------------------------------------------

    [Fact]
    public void A_snippet_can_be_moved_to_another_folder()
    {
        FolderNode source = FolderWith("A", "B");
        FolderNode destination = FolderWith("X");
        SnippetNode moving = source.Snippets[0];

        _model.MoveSnippet(moving, destination, -1);

        Assert.Equal(["B"], source.Snippets.Select(snippet => snippet.Title));
        Assert.Equal(["X", "A"], destination.Snippets.Select(snippet => snippet.Title));
        Assert.Equal(destination.Id, moving.FolderId);
    }

    [Fact]
    public void A_cross_folder_move_is_written_through_to_both_folders()
    {
        FolderNode source = FolderWith("A", "B");
        FolderNode destination = FolderWith("X");

        _model.MoveSnippet(source.Snippets[0], destination, 0);

        IReadOnlyList<SnippetFolderDetail> stored = Stored();
        Assert.Equal(["B"], stored[0].Snippets.Select(snippet => snippet.Title));
        Assert.Equal(["A", "X"], stored[1].Snippets.Select(snippet => snippet.Title));
    }

    [Fact]
    public void A_moved_snippet_keeps_its_content()
    {
        FolderNode source = FolderWith("A");
        FolderNode destination = _model.AddFolder();
        SnippetNode moving = source.Snippets[0];
        _model.UpdateContent(moving, "body");

        _model.MoveSnippet(moving, destination, -1);

        Assert.Equal("body", _store.FetchSnippet(moving.Id)!.Content);
    }

    // ---- Import and export -------------------------------------------------------------------------

    [Fact]
    public void Imported_folders_appear_in_the_tree()
    {
        int imported = _model.Import(
            "<folders><folder><title>Imported</title><snippets>" +
            "<snippet><title>S</title><content>body</content></snippet>" +
            "</snippets></folder></folders>");

        Assert.Equal(1, imported);
        Assert.Equal("Imported", Assert.Single(_model.Folders).Title);
        Assert.Equal("S", Assert.Single(_model.Folders[0].Snippets).Title);
    }

    [Fact]
    public void A_malformed_import_is_reported_and_changes_nothing()
    {
        _model.AddFolder();

        Assert.Equal(-1, _model.Import("<folders><folder>"));
        Assert.Single(_model.Folders);
    }

    [Fact]
    public void Export_round_trips_through_import()
    {
        FolderNode folder = FolderWith("A");
        _model.Rename(folder, "Signatures");
        _model.UpdateContent(folder.Snippets[0], "Kind regards,\r\nAlex");

        string xml = _model.Export();

        SnippetEditorViewModel other = new(NewStore());
        other.Import(xml);

        Assert.Equal("Signatures", Assert.Single(other.Folders).Title);
        Assert.Equal("Kind regards,\r\nAlex", other.Folders[0].Snippets[0].Content);
    }

    // ---- Change notification ---------------------------------------------------------------------------

    [Fact]
    public void Every_mutation_raises_changed_so_hotkeys_and_menus_can_react()
    {
        int changes = 0;
        _model.Changed += (_, _) => changes++;

        FolderNode folder = _model.AddFolder();
        _model.Rename(folder, "F");
        SnippetNode snippet = _model.AddSnippet(folder)!;
        _model.UpdateContent(snippet, "x");
        _model.ToggleEnabled(snippet);
        _model.Delete(snippet);

        Assert.Equal(6, changes);
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new SnippetEditorViewModel(null!));
        Assert.Throws<ArgumentNullException>(() => _model.Rename(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => _model.Delete(null!));
        Assert.Throws<ArgumentNullException>(() => _model.MoveFolder(null!, 0));
    }

    private SqliteSnippetStore NewStore()
    {
        ClipDatabase database = ClipDatabase.InMemory($"editor-other-{Guid.NewGuid():n}");
        database.Migrate();
        return new SqliteSnippetStore(database);
    }
}
