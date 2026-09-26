using System.Text;

namespace CopyPasta.Data.Tests;

/// <summary>
/// Full-text search over clip titles and recognised text, and the byte-budget trimming.
/// </summary>
public sealed class ClipSearchTests : IDisposable
{
    private readonly ClipDatabase _database;
    private readonly SqliteClipStore _store;

    public ClipSearchTests()
    {
        _database = ClipDatabase.InMemory($"search-{Guid.NewGuid():n}");
        _database.Migrate();
        _store = new SqliteClipStore(_database);
    }

    public void Dispose() => _database.Dispose();

    private string Save(string title, long updatedAt = 1000, int assetBytes = 4)
    {
        string id = Guid.NewGuid().ToString("n");

        _store.Save(new StoredClip
        {
            Id = id,
            ContentHash = id,
            Title = title,
            Formats = [ClipboardFormat.UnicodeText],
            Assets = [new ClipAsset(ClipboardFormat.UnicodeText, new byte[assetBytes])],
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt,
            IsConcealed = false,
            IsFromCloudClipboard = false,
        });

        return id;
    }

    private string[] Find(string query) =>
        _store.Search(query, ClipOrder.UpdatedAt, 50).Select(clip => clip.Title).ToArray();

    // ---- Searching titles ----------------------------------------------------------------

    [Fact]
    public void A_clip_is_found_by_a_word_in_its_title()
    {
        Save("the quick brown fox");
        Save("something else entirely");

        Assert.Equal(["the quick brown fox"], Find("brown"));
    }

    [Fact]
    public void Search_matches_a_prefix_so_results_narrow_as_you_type()
    {
        Save("refactoring notes");

        Assert.Single(Find("refac"));
        Assert.Single(Find("refactoring"));
    }

    [Fact]
    public void Search_is_case_insensitive()
    {
        Save("Deployment Checklist");

        Assert.Single(Find("deployment"));
        Assert.Single(Find("DEPLOYMENT"));
    }

    [Fact]
    public void Several_words_all_have_to_match()
    {
        Save("quick brown fox");
        Save("quick red herring");

        Assert.Equal(["quick brown fox"], Find("quick brown"));
    }

    [Fact]
    public void A_query_that_matches_nothing_returns_nothing()
    {
        Save("hello");

        Assert.Empty(Find("goodbye"));
    }

    [Fact]
    public void Results_come_back_newest_first()
    {
        Save("note one", updatedAt: 100);
        Save("note two", updatedAt: 200);

        Assert.Equal(["note two", "note one"], Find("note"));
    }

    [Fact]
    public void The_limit_is_honoured()
    {
        for (int index = 0; index < 10; index++)
        {
            Save($"note {index}", updatedAt: index);
        }

        Assert.Equal(3, _store.Search("note", ClipOrder.UpdatedAt, 3).Count);
    }

    [Fact]
    public void Non_ASCII_text_is_searchable()
    {
        Save("café 日本語 notes");

        Assert.Single(Find("café"));
    }

    // ---- Searching recognised text ----------------------------------------------------------

    [Fact]
    public void A_clip_is_found_by_text_recognised_in_its_image()
    {
        // The Phase 7 criterion: OCR text is searchable.
        string id = Save("(Image)");
        _store.SaveOcrText(id, "INVOICE 2026-0042 total 199.00");

        Assert.Equal(["(Image)"], Find("invoice"));
        Assert.Single(Find("0042"));
    }

    [Fact]
    public void Recognised_text_can_be_replaced()
    {
        string id = Save("(Image)");
        _store.SaveOcrText(id, "first reading");
        _store.SaveOcrText(id, "second reading");

        Assert.Empty(Find("first"));
        Assert.Single(Find("second"));
    }

    [Fact]
    public void Saving_recognised_text_for_an_unknown_clip_reports_that_nothing_happened()
    {
        Assert.False(_store.SaveOcrText("nope", "text"));
    }

    [Fact]
    public void Clips_awaiting_recognition_are_listed_until_they_have_text()
    {
        // Lets a restart pick up work interrupted by a close.
        string first = Save("(Image) one");
        Save("(Image) two");

        Assert.Equal(2, _store.FetchClipsAwaitingOcr(10).Count);

        _store.SaveOcrText(first, string.Empty);

        Assert.Single(_store.FetchClipsAwaitingOcr(10));
    }

    [Fact]
    public void An_empty_recognition_result_still_counts_as_attempted()
    {
        // Otherwise a picture of a sunset would be re-scanned on every start, forever.
        string id = Save("(Image)");
        _store.SaveOcrText(id, string.Empty);

        Assert.Empty(_store.FetchClipsAwaitingOcr(10));
    }

    // ---- Index maintenance ---------------------------------------------------------------------

    [Fact]
    public void Deleting_a_clip_removes_it_from_the_index()
    {
        string id = Save("findable");
        _store.Delete(id);

        Assert.Empty(Find("findable"));
    }

    [Fact]
    public void Clearing_the_history_empties_the_index()
    {
        Save("findable");
        _store.DeleteAll();

        Assert.Empty(Find("findable"));
    }

    [Fact]
    public void Trimming_removes_the_trimmed_clips_from_the_index()
    {
        // Trimming deletes by a different route than Delete; the trigger covers both.
        Save("keeper", updatedAt: 200);
        Save("doomed", updatedAt: 100);

        _store.DeleteOverflowing(ClipOrder.UpdatedAt, 1);

        Assert.Empty(Find("doomed"));
        Assert.Single(Find("keeper"));
    }

    [Fact]
    public void Re_saving_a_clip_updates_its_indexed_title()
    {
        string id = Save("original");

        _store.Save(new StoredClip
        {
            Id = id,
            ContentHash = id,
            Title = "revised",
            Formats = [ClipboardFormat.UnicodeText],
            Assets = [new ClipAsset(ClipboardFormat.UnicodeText, [1])],
            CreatedAt = 1000,
            UpdatedAt = 2000,
            IsConcealed = false,
            IsFromCloudClipboard = false,
        });

        Assert.Empty(Find("original"));
        Assert.Single(Find("revised"));
    }

    // ---- Query sanitising -------------------------------------------------------------------------

    [Theory]
    [InlineData("c++")]
    [InlineData("\"unbalanced")]
    [InlineData("a AND OR b")]
    [InlineData("NEAR(")]
    [InlineData("*")]
    [InlineData("^")]
    public void Punctuation_that_is_FTS5_syntax_does_not_throw(string query)
    {
        // Typing any of these into a search box must search, not crash.
        Save("hello");

        Exception? failure = Record.Exception(() => _store.Search(query, ClipOrder.UpdatedAt, 10));

        Assert.Null(failure);
    }

    [Fact]
    public void A_query_of_only_punctuation_finds_nothing()
    {
        Save("hello");

        Assert.Empty(Find("!!!"));
        Assert.Empty(Find("   "));
        Assert.Empty(Find(string.Empty));
    }

    [Fact]
    public void Words_survive_the_sanitising()
    {
        Assert.Equal("\"hello\"*", SqliteClipStore.BuildMatchExpression("hello"));
        Assert.Equal("\"hello\"* \"world\"*", SqliteClipStore.BuildMatchExpression("hello world"));
        Assert.Equal("\"c\"*", SqliteClipStore.BuildMatchExpression("c++"));
        Assert.Equal(string.Empty, SqliteClipStore.BuildMatchExpression("!!!"));
    }

    [Fact]
    public void A_non_positive_limit_returns_nothing()
    {
        Save("hello");

        Assert.Empty(_store.Search("hello", ClipOrder.UpdatedAt, 0));
    }

    // ---- Byte-budget trimming ------------------------------------------------------------------------

    [Fact]
    public void Trimming_by_bytes_removes_the_oldest_until_it_fits()
    {
        // The gap macOS leaves: it trims by row count only, so thirty screenshots are thirty rows
        // and several hundred megabytes.
        Save("newest", updatedAt: 300, assetBytes: 400);
        Save("middle", updatedAt: 200, assetBytes: 400);
        Save("oldest", updatedAt: 100, assetBytes: 400);

        int removed = _store.DeleteOverflowingBytes(ClipOrder.UpdatedAt, 900);

        Assert.Equal(1, removed);
        Assert.Equal(
            ["newest", "middle"],
            _store.FetchRecent(ClipOrder.UpdatedAt, 10).Select(clip => clip.Title));
    }

    [Fact]
    public void Trimming_by_bytes_leaves_a_history_that_already_fits_alone()
    {
        Save("a", assetBytes: 10);
        Save("b", assetBytes: 10);

        Assert.Equal(0, _store.DeleteOverflowingBytes(ClipOrder.UpdatedAt, 10_000));
        Assert.Equal(2, _store.Count());
    }

    [Fact]
    public void The_newest_clip_is_kept_even_when_it_alone_exceeds_the_budget()
    {
        // Deleting what the user just copied because it was large would be worse than briefly
        // exceeding the budget.
        Save("huge", updatedAt: 200, assetBytes: 5000);
        Save("old", updatedAt: 100, assetBytes: 10);

        _store.DeleteOverflowingBytes(ClipOrder.UpdatedAt, 100);

        Assert.Equal(["huge"], _store.FetchRecent(ClipOrder.UpdatedAt, 10).Select(clip => clip.Title));
    }

    [Fact]
    public void Thumbnails_count_towards_the_budget()
    {
        string id = Save("with preview", assetBytes: 10);
        _store.SaveThumbnail(id, "Image", new byte[500]);
        Save("newest", updatedAt: 5000, assetBytes: 10);

        _store.DeleteOverflowingBytes(ClipOrder.UpdatedAt, 200);

        Assert.Equal(["newest"], _store.FetchRecent(ClipOrder.UpdatedAt, 10).Select(clip => clip.Title));
    }

    [Fact]
    public void A_non_positive_budget_trims_nothing()
    {
        Save("a", assetBytes: 1000);

        Assert.Equal(0, _store.DeleteOverflowingBytes(ClipOrder.UpdatedAt, 0));
        Assert.Equal(1, _store.Count());
    }

    [Fact]
    public void Trimming_by_bytes_honours_the_sort_order()
    {
        Save("recent but old content", updatedAt: 300, assetBytes: 400);

        string id = Guid.NewGuid().ToString("n");
        _store.Save(new StoredClip
        {
            Id = id,
            ContentHash = id,
            Title = "created later",
            Formats = [ClipboardFormat.UnicodeText],
            Assets = [new ClipAsset(ClipboardFormat.UnicodeText, new byte[400])],
            CreatedAt = 9000,
            UpdatedAt = 100,
            IsConcealed = false,
            IsFromCloudClipboard = false,
        });

        _store.DeleteOverflowingBytes(ClipOrder.CreatedAt, 500);

        Assert.Equal(
            ["created later"],
            _store.FetchRecent(ClipOrder.CreatedAt, 10).Select(clip => clip.Title));
    }

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => _store.Search(null!, ClipOrder.UpdatedAt, 10));
        Assert.Throws<ArgumentNullException>(() => _store.SaveOcrText("id", null!));
        Assert.Throws<ArgumentException>(() => _store.SaveOcrText(string.Empty, "text"));
    }
}
