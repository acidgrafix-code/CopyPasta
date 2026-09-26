using System.Text;

namespace CopyPasta.Data.Tests;

/// <summary>
/// Storage behaviour against a real SQLite database. Each test gets its own in-memory database.
/// </summary>
public sealed class SqliteClipStoreTests : IDisposable
{
    private readonly ClipDatabase _database;
    private readonly SqliteClipStore _store;

    public SqliteClipStoreTests()
    {
        _database = ClipDatabase.InMemory($"copypasta-tests-{Guid.NewGuid():n}");
        _database.Migrate();
        _store = new SqliteClipStore(_database);
    }

    public void Dispose() => _database.Dispose();

    private static ClipAsset TextAsset(string value) =>
        new(ClipboardFormat.UnicodeText, Encoding.Unicode.GetBytes(value + '\0'));

    private static StoredClip Clip(
        string id,
        string title = "hello",
        long createdAt = 1000,
        long updatedAt = 1000,
        IReadOnlyList<ClipAsset>? assets = null,
        bool isConcealed = false,
        bool isFromCloud = false)
    {
        assets ??= [TextAsset(title)];
        return new StoredClip
        {
            Id = id,
            ContentHash = id,
            Title = title,
            Formats = assets.Select(asset => asset.Format).ToArray(),
            Assets = assets,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
            IsConcealed = isConcealed,
            IsFromCloudClipboard = isFromCloud,
        };
    }

    // ---- Schema ------------------------------------------------------------------------

    [Fact]
    public void Migrating_twice_is_harmless()
    {
        _database.Migrate();
        _database.Migrate();

        Assert.Equal(0, _store.Count());
    }

    // ---- Save and read back -------------------------------------------------------------

    [Fact]
    public void A_saved_clip_round_trips()
    {
        _store.Save(Clip("abc", "hello world"));

        Assert.True(_store.Exists("abc"));
        Assert.Equal(1, _store.Count());

        ClipContent? content = _store.FetchContent("abc");
        Assert.NotNull(content);
        Assert.Equal("hello world", content.TextValue);
    }

    [Fact]
    public void Blobs_round_trip_byte_for_byte()
    {
        // The whole point of storing raw blobs: CF_HTML carries byte offsets in its own header,
        // so anything short of an exact round-trip corrupts it.
        byte[] payload = new byte[4096];
        Random.Shared.NextBytes(payload);

        _store.Save(Clip("abc", assets: [new ClipAsset(ClipboardFormat.Html, payload)]));

        ClipContent? content = _store.FetchContent("abc");
        Assert.NotNull(content);
        Assert.Equal(payload, content.DataFor(ClipboardFormat.Html));
    }

    [Fact]
    public void Asset_order_is_preserved()
    {
        ClipAsset[] assets =
        [
            new(ClipboardFormat.Png, [1]),
            new(ClipboardFormat.Html, [2]),
            TextAsset("hi"),
        ];

        _store.Save(Clip("abc", assets: assets));

        ClipContent? content = _store.FetchContent("abc");
        Assert.NotNull(content);
        Assert.Equal(
            new[] { ClipboardFormat.Png, ClipboardFormat.Html, ClipboardFormat.UnicodeText },
            content.Formats.ToArray());
    }

    [Fact]
    public void Formats_are_recorded_on_the_summary_row_so_the_menu_need_not_touch_blobs()
    {
        _store.Save(Clip("abc", assets: [new ClipAsset(ClipboardFormat.Png, [1]), TextAsset("hi")]));

        ClipSummary summary = Assert.Single(_store.FetchRecent(ClipOrder.UpdatedAt, 10));
        Assert.Equal(
            new[] { ClipboardFormat.Png, ClipboardFormat.UnicodeText },
            summary.Formats.ToArray());
        Assert.Equal(ClipboardFormat.Png, summary.PrimaryFormat);
    }

    [Fact]
    public void Marker_flags_round_trip()
    {
        _store.Save(Clip("abc", isConcealed: true, isFromCloud: true));

        ClipSummary summary = Assert.Single(_store.FetchRecent(ClipOrder.UpdatedAt, 10));
        Assert.True(summary.IsConcealed);
        Assert.True(summary.IsFromCloudClipboard);
    }

    [Fact]
    public void An_unknown_id_has_no_content()
    {
        Assert.Null(_store.FetchContent("nope"));
        Assert.False(_store.Exists("nope"));
    }

    // ---- Upsert -------------------------------------------------------------------------

    [Fact]
    public void Re_saving_the_same_id_updates_the_timestamp_without_duplicating_the_row()
    {
        _store.Save(Clip("abc", createdAt: 1000, updatedAt: 1000));
        _store.Save(Clip("abc", createdAt: 2000, updatedAt: 2000));

        ClipSummary summary = Assert.Single(_store.FetchRecent(ClipOrder.UpdatedAt, 10));
        Assert.Equal(2000, summary.UpdatedAt);
    }

    [Fact]
    public void Re_saving_preserves_the_original_created_at()
    {
        // "Sort by date created" is meaningless if re-copying an old clip resets its birthday.
        _store.Save(Clip("abc", createdAt: 1000, updatedAt: 1000));
        _store.Save(Clip("abc", createdAt: 2000, updatedAt: 2000));

        ClipSummary summary = Assert.Single(_store.FetchRecent(ClipOrder.UpdatedAt, 10));
        Assert.Equal(1000, summary.CreatedAt);
    }

    [Fact]
    public void Re_saving_does_not_duplicate_the_assets()
    {
        _store.Save(Clip("abc"));
        _store.Save(Clip("abc"));
        _store.Save(Clip("abc"));

        ClipContent? content = _store.FetchContent("abc");
        Assert.NotNull(content);
        Assert.Single(content.Assets);
    }

    [Fact]
    public void Re_saving_within_the_same_second_does_not_throw()
    {
        // Regression guard: an earlier implementation inferred "row already existed" by
        // comparing timestamps, which broke when two copies landed in the same second.
        _store.Save(Clip("abc", createdAt: 1000, updatedAt: 1000));
        _store.Save(Clip("abc", createdAt: 1000, updatedAt: 1000));

        Assert.Equal(1, _store.Count());
        Assert.Single(_store.FetchContent("abc")!.Assets);
    }

    // ---- Touch --------------------------------------------------------------------------

    [Fact]
    public void Touching_a_clip_moves_it_to_the_top_of_the_last_used_ordering()
    {
        _store.Save(Clip("a", createdAt: 1, updatedAt: 1));
        _store.Save(Clip("b", createdAt: 2, updatedAt: 2));

        Assert.True(_store.Touch("a", 99));

        Assert.Equal(
            new[] { "a", "b" },
            _store.FetchRecent(ClipOrder.UpdatedAt, 10).Select(clip => clip.Id).ToArray());
    }

    [Fact]
    public void Touching_a_clip_leaves_its_creation_time_and_blobs_alone()
    {
        _store.Save(Clip("a", createdAt: 1, updatedAt: 1));

        _store.Touch("a", 99);

        ClipSummary summary = Assert.Single(_store.FetchRecent(ClipOrder.UpdatedAt, 10));
        Assert.Equal(1, summary.CreatedAt);
        Assert.Single(_store.FetchContent("a")!.Assets);
    }

    [Fact]
    public void Touching_an_unknown_clip_reports_that_nothing_happened()
    {
        Assert.False(_store.Touch("nope", 99));
    }

    // ---- Ordering -----------------------------------------------------------------------

    [Fact]
    public void Recent_clips_come_back_newest_first_by_last_used()
    {
        _store.Save(Clip("old", createdAt: 3000, updatedAt: 1000));
        _store.Save(Clip("new", createdAt: 1000, updatedAt: 3000));

        Assert.Equal(
            new[] { "new", "old" },
            _store.FetchRecent(ClipOrder.UpdatedAt, 10).Select(clip => clip.Id).ToArray());
    }

    [Fact]
    public void Recent_clips_come_back_newest_first_by_date_created()
    {
        _store.Save(Clip("old", createdAt: 3000, updatedAt: 1000));
        _store.Save(Clip("new", createdAt: 1000, updatedAt: 3000));

        Assert.Equal(
            new[] { "old", "new" },
            _store.FetchRecent(ClipOrder.CreatedAt, 10).Select(clip => clip.Id).ToArray());
    }

    [Fact]
    public void The_limit_is_honoured()
    {
        for (int index = 0; index < 10; index++)
        {
            _store.Save(Clip($"clip{index}", updatedAt: index));
        }

        Assert.Equal(3, _store.FetchRecent(ClipOrder.UpdatedAt, 3).Count);
    }

    [Fact]
    public void A_non_positive_limit_returns_nothing()
    {
        _store.Save(Clip("abc"));

        Assert.Empty(_store.FetchRecent(ClipOrder.UpdatedAt, 0));
        Assert.Empty(_store.FetchRecent(ClipOrder.UpdatedAt, -5));
    }

    // ---- Delete -------------------------------------------------------------------------

    [Fact]
    public void Deleting_a_clip_removes_its_assets_too()
    {
        _store.Save(Clip("abc"));

        Assert.True(_store.Delete("abc"));
        Assert.Equal(0, _store.Count());
        Assert.Equal(0, _store.TotalAssetBytes());
    }

    [Fact]
    public void Deleting_an_unknown_clip_reports_that_nothing_happened()
    {
        Assert.False(_store.Delete("nope"));
    }

    [Fact]
    public void Delete_all_clears_everything()
    {
        _store.Save(Clip("a"));
        _store.Save(Clip("b"));

        _store.DeleteAll();

        Assert.Equal(0, _store.Count());
        Assert.Equal(0, _store.TotalAssetBytes());
        Assert.False(_store.Any());
    }

    // ---- Trimming -----------------------------------------------------------------------

    [Fact]
    public void Trimming_keeps_the_newest_clips_by_last_used()
    {
        for (int index = 0; index < 10; index++)
        {
            _store.Save(Clip($"clip{index}", updatedAt: index));
        }

        Assert.Equal(7, _store.DeleteOverflowing(ClipOrder.UpdatedAt, 3));

        Assert.Equal(
            new[] { "clip9", "clip8", "clip7" },
            _store.FetchRecent(ClipOrder.UpdatedAt, 10).Select(clip => clip.Id).ToArray());
    }

    [Fact]
    public void Trimming_by_the_other_order_keeps_a_different_set()
    {
        // The reason both orders are indexed: trimming with the order the menu is not using
        // silently deletes the clips the user can actually see.
        _store.Save(Clip("a", createdAt: 1, updatedAt: 9));
        _store.Save(Clip("b", createdAt: 9, updatedAt: 1));

        _store.DeleteOverflowing(ClipOrder.CreatedAt, 1);

        Assert.Equal("b", Assert.Single(_store.FetchRecent(ClipOrder.CreatedAt, 10)).Id);
    }

    [Fact]
    public void Trimming_below_the_limit_deletes_nothing()
    {
        _store.Save(Clip("a"));

        Assert.Equal(0, _store.DeleteOverflowing(ClipOrder.UpdatedAt, 30));
        Assert.Equal(1, _store.Count());
    }

    [Fact]
    public void A_non_positive_limit_wipes_the_history()
    {
        _store.Save(Clip("a"));
        _store.Save(Clip("b"));

        Assert.Equal(2, _store.DeleteOverflowing(ClipOrder.UpdatedAt, 0));
        Assert.Equal(0, _store.Count());
    }

    // ---- Byte accounting -----------------------------------------------------------------

    [Fact]
    public void Total_asset_bytes_sums_every_blob()
    {
        _store.Save(Clip("a", assets: [new ClipAsset(ClipboardFormat.Png, new byte[100])]));
        _store.Save(Clip("b", assets: [new ClipAsset(ClipboardFormat.Png, new byte[250])]));

        Assert.Equal(350, _store.TotalAssetBytes());
    }

    [Fact]
    public void An_empty_database_reports_zero_bytes()
    {
        Assert.Equal(0, _store.TotalAssetBytes());
        Assert.False(_store.Any());
    }

    // ---- Argument validation -------------------------------------------------------------

    [Fact]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new SqliteClipStore(null!));
        Assert.Throws<ArgumentNullException>(() => _store.Save(null!));
        Assert.Throws<ArgumentException>(() => _store.Exists(string.Empty));
        Assert.Throws<ArgumentException>(() => _store.FetchContent(string.Empty));
        Assert.Throws<ArgumentException>(() => new ClipDatabase("  "));
    }
}
