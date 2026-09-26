using CopyPasta.Core.Snippets;

namespace CopyPasta.Data.Tests;

/// <summary>Snippet storage against a real SQLite database.</summary>
public sealed class SqliteSnippetStoreTests : IDisposable
{
    private readonly ClipDatabase _database;
    private readonly SqliteSnippetStore _store;

    public SqliteSnippetStoreTests()
    {
        _database = ClipDatabase.InMemory($"snippets-{Guid.NewGuid():n}");
        _database.Migrate();
        _store = new SqliteSnippetStore(_database);
    }

    public void Dispose() => _database.Dispose();

    private SnippetFolder FolderWith(params string[] snippetTitles)
    {
        SnippetFolder folder = _store.InsertFolder();

        foreach (string title in snippetTitles)
        {
            Snippet snippet = _store.InsertSnippet(folder.Id)!;
            _store.UpdateSnippetTitle(snippet.Id, title);
        }

        return folder;
    }

    private SnippetFolderDetail Detail(Guid folderId) => _store.FetchFolderDetail(folderId)!;

    // ---- Folders -------------------------------------------------------------------------

    [Fact]
    public void A_new_folder_gets_the_default_title_and_is_enabled()
    {
        SnippetFolder folder = _store.InsertFolder();

        Assert.Equal("untitled folder", folder.Title);
        Assert.True(folder.IsEnabled);
        Assert.Equal(0, folder.Index);
    }

    [Fact]
    public void Folders_are_appended_in_order()
    {
        _store.InsertFolder();
        _store.InsertFolder();
        _store.InsertFolder();

        Assert.Equal([0, 1, 2], _store.FetchFolderDetails().Select(detail => detail.Folder.Index));
    }

    [Fact]
    public void A_folder_title_can_be_changed()
    {
        SnippetFolder folder = _store.InsertFolder();

        _store.UpdateFolderTitle(folder.Id, "Signatures");

        Assert.Equal("Signatures", Detail(folder.Id).Folder.Title);
    }

    [Fact]
    public void A_folder_can_be_disabled_without_losing_its_snippets()
    {
        SnippetFolder folder = FolderWith("A", "B");

        _store.UpdateFolderIsEnabled(folder.Id, false);

        SnippetFolderDetail detail = Detail(folder.Id);
        Assert.False(detail.Folder.IsEnabled);
        Assert.Equal(2, detail.Snippets.Count);
    }

    [Fact]
    public void Deleting_a_folder_takes_its_snippets_with_it()
    {
        SnippetFolder folder = FolderWith("A", "B");

        Assert.True(_store.DeleteFolder(folder.Id));

        Assert.Null(_store.FetchFolderDetail(folder.Id));
        Assert.Empty(_store.FetchFolderDetails());
    }

    [Fact]
    public void Deleting_an_unknown_folder_reports_that_nothing_happened()
    {
        Assert.False(_store.DeleteFolder(Guid.NewGuid()));
    }

    [Fact]
    public void Folders_can_be_reordered()
    {
        SnippetFolder first = _store.InsertFolder();
        SnippetFolder second = _store.InsertFolder();
        SnippetFolder third = _store.InsertFolder();

        _store.UpdateFolderOrder([third.Id, first.Id, second.Id]);

        Assert.Equal(
            [third.Id, first.Id, second.Id],
            _store.FetchFolderDetails().Select(detail => detail.Folder.Id));
    }

    // ---- Snippets --------------------------------------------------------------------------

    [Fact]
    public void A_new_snippet_gets_the_default_title_and_empty_content()
    {
        SnippetFolder folder = _store.InsertFolder();

        Snippet snippet = _store.InsertSnippet(folder.Id)!;

        Assert.Equal("untitled snippet", snippet.Title);
        Assert.Equal(string.Empty, snippet.Content);
        Assert.True(snippet.IsEnabled);
    }

    [Fact]
    public void Snippets_are_appended_within_their_folder()
    {
        SnippetFolder folder = FolderWith("A", "B", "C");

        Assert.Equal(["A", "B", "C"], Detail(folder.Id).Snippets.Select(snippet => snippet.Title));
    }

    [Fact]
    public void Each_folder_numbers_its_snippets_from_zero()
    {
        SnippetFolder first = FolderWith("A", "B");
        SnippetFolder second = FolderWith("C");

        Assert.Equal([0, 1], Detail(first.Id).Snippets.Select(snippet => snippet.Index));
        Assert.Equal([0], Detail(second.Id).Snippets.Select(snippet => snippet.Index));
    }

    [Fact]
    public void A_snippet_in_an_unknown_folder_is_refused()
    {
        Assert.Null(_store.InsertSnippet(Guid.NewGuid()));
    }

    [Fact]
    public void Snippet_content_round_trips_including_line_breaks()
    {
        SnippetFolder folder = _store.InsertFolder();
        Snippet snippet = _store.InsertSnippet(folder.Id)!;
        const string content = "line one\r\n    indented\n";

        _store.UpdateSnippetContent(snippet.Id, content);

        Assert.Equal(content, _store.FetchSnippet(snippet.Id)!.Content);
    }

    [Fact]
    public void A_snippet_can_be_disabled()
    {
        SnippetFolder folder = FolderWith("A");
        Snippet snippet = Detail(folder.Id).Snippets[0];

        _store.UpdateSnippetIsEnabled(snippet.Id, false);

        Assert.False(_store.FetchSnippet(snippet.Id)!.IsEnabled);
        Assert.Empty(Detail(folder.Id).EnabledSnippets);
    }

    [Fact]
    public void Snippets_can_be_reordered_within_a_folder()
    {
        SnippetFolder folder = FolderWith("A", "B", "C");
        IReadOnlyList<Snippet> snippets = Detail(folder.Id).Snippets;

        _store.UpdateSnippetOrder([snippets[2].Id, snippets[0].Id, snippets[1].Id]);

        Assert.Equal(["C", "A", "B"], Detail(folder.Id).Snippets.Select(snippet => snippet.Title));
    }

    [Fact]
    public void Deleting_a_snippet_leaves_the_rest_alone()
    {
        SnippetFolder folder = FolderWith("A", "B", "C");
        Snippet middle = Detail(folder.Id).Snippets[1];

        Assert.True(_store.DeleteSnippet(middle.Id));

        Assert.Equal(["A", "C"], Detail(folder.Id).Snippets.Select(snippet => snippet.Title));
    }

    // ---- Moving between folders ----------------------------------------------------------------

    [Fact]
    public void A_snippet_can_be_moved_to_another_folder()
    {
        SnippetFolder source = FolderWith("A", "B");
        SnippetFolder destination = FolderWith("X");

        Snippet moving = Detail(source.Id).Snippets[0];
        Snippet existing = Detail(destination.Id).Snippets[0];

        _store.MoveSnippet(moving.Id, destination.Id, [existing.Id, moving.Id]);

        Assert.Equal(["B"], Detail(source.Id).Snippets.Select(snippet => snippet.Title));
        Assert.Equal(["X", "A"], Detail(destination.Id).Snippets.Select(snippet => snippet.Title));
    }

    [Fact]
    public void A_moved_snippet_lands_in_the_position_it_was_dropped()
    {
        // The move and the reorder are one transaction; a snippet that kept its old ordinal would
        // sort unpredictably in its new folder.
        SnippetFolder source = FolderWith("A");
        SnippetFolder destination = FolderWith("X", "Y");

        Snippet moving = Detail(source.Id).Snippets[0];
        IReadOnlyList<Snippet> existing = Detail(destination.Id).Snippets;

        _store.MoveSnippet(moving.Id, destination.Id, [existing[0].Id, moving.Id, existing[1].Id]);

        Assert.Equal(["X", "A", "Y"], Detail(destination.Id).Snippets.Select(snippet => snippet.Title));
    }

    [Fact]
    public void A_moved_snippet_keeps_its_content()
    {
        SnippetFolder source = FolderWith("A");
        SnippetFolder destination = _store.InsertFolder();
        Snippet moving = Detail(source.Id).Snippets[0];
        _store.UpdateSnippetContent(moving.Id, "body text");

        _store.MoveSnippet(moving.Id, destination.Id, [moving.Id]);

        Assert.Equal("body text", _store.FetchSnippet(moving.Id)!.Content);
    }

    // ---- Import ------------------------------------------------------------------------------------

    [Fact]
    public void Imported_folders_arrive_with_their_snippets_in_order()
    {
        IReadOnlyList<SnippetFolderDetail> inserted = _store.InsertFolders(
        [
            new ImportedFolder("Signatures",
            [
                new ImportedSnippet("Formal", "Kind regards,"),
                new ImportedSnippet("Casual", "Cheers,"),
            ]),
            new ImportedFolder("Empty", []),
        ]);

        Assert.Equal(2, inserted.Count);
        Assert.Equal(["Formal", "Casual"], inserted[0].Snippets.Select(snippet => snippet.Title));
        Assert.Equal("Kind regards,", inserted[0].Snippets[0].Content);
        Assert.Empty(inserted[1].Snippets);
    }

    [Fact]
    public void Imported_folders_are_appended_after_existing_ones()
    {
        _store.UpdateFolderTitle(_store.InsertFolder().Id, "Existing");

        _store.InsertFolders([new ImportedFolder("Imported", [])]);

        Assert.Equal(
            ["Existing", "Imported"],
            _store.FetchFolderDetails().Select(detail => detail.Folder.Title));
    }

    [Fact]
    public void Imported_items_arrive_enabled()
    {
        // The interchange format carries no enabled flag, so everything imported is usable.
        IReadOnlyList<SnippetFolderDetail> inserted = _store.InsertFolders(
            [new ImportedFolder("F", [new ImportedSnippet("S", "body")])]);

        Assert.True(inserted[0].Folder.IsEnabled);
        Assert.True(inserted[0].Snippets[0].IsEnabled);
    }

    [Fact]
    public void Importing_nothing_changes_nothing()
    {
        Assert.Empty(_store.InsertFolders([]));
        Assert.Empty(_store.FetchFolderDetails());
    }

    // ---- Export round trip ---------------------------------------------------------------------------

    [Fact]
    public void A_library_survives_export_and_re_import()
    {
        // The end-to-end interchange path: store, export to XML, import back, compare.
        _store.InsertFolders(
        [
            new ImportedFolder("Signatures",
            [
                new ImportedSnippet("Formal", "Kind regards,\r\n  Alex"),
                new ImportedSnippet("Casual", "Cheers,"),
            ]),
            new ImportedFolder("R&D", [new ImportedSnippet("<tag>", "a & b")]),
        ]);

        string xml = SnippetXml.Export(_store.FetchFolderDetails());

        using ClipDatabase other = ClipDatabase.InMemory($"snippets-other-{Guid.NewGuid():n}");
        other.Migrate();
        SqliteSnippetStore destination = new(other);
        destination.InsertFolders(SnippetXml.Import(xml));

        IReadOnlyList<SnippetFolderDetail> restored = destination.FetchFolderDetails();

        Assert.Equal(["Signatures", "R&D"], restored.Select(detail => detail.Folder.Title));
        Assert.Equal("Kind regards,\r\n  Alex", restored[0].Snippets[0].Content);
        Assert.Equal("<tag>", restored[1].Snippets[0].Title);
        Assert.Equal("a & b", restored[1].Snippets[0].Content);
    }

    // ---- Argument validation ---------------------------------------------------------------------------

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new SqliteSnippetStore(null!));
        Assert.Throws<ArgumentNullException>(() => _store.InsertFolders(null!));
        Assert.Throws<ArgumentNullException>(() => _store.UpdateFolderTitle(Guid.NewGuid(), null!));
        Assert.Throws<ArgumentNullException>(() => _store.UpdateSnippetContent(Guid.NewGuid(), null!));
        Assert.Throws<ArgumentNullException>(() => _store.UpdateFolderOrder(null!));
    }
}
