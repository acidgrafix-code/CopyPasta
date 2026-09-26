using CopyPasta.Core.Capture;
using System.IO;
using CopyPasta.Core.Clipboard;
using CopyPasta.Core.Ocr;
using CopyPasta.Data;

namespace CopyPasta.App.Tests;

/// <summary>A recogniser the test drives.</summary>
public sealed class FakeTextRecognizer : IImageTextRecognizer
{
    private readonly SemaphoreSlim _calls = new(0);

    public bool IsAvailable { get; set; } = true;

    /// <summary>What to return; null means recognition failed.</summary>
    public string? Result { get; set; } = "recognised text";

    public List<int> ReceivedImageSizes { get; } = [];

    public Task<string?> RecognizeAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        lock (ReceivedImageSizes)
        {
            ReceivedImageSizes.Add(imageBytes.Length);
        }

        _calls.Release();
        return Task.FromResult(Result);
    }

    /// <summary>Waits for the next recognition to start, rather than sleeping and hoping.</summary>
    public Task<bool> WaitForCallAsync(TimeSpan timeout) => _calls.WaitAsync(timeout);
}

public sealed class OcrQueueTests : IDisposable
{
    private readonly ClipDatabase _database;
    private readonly SqliteClipStore _store;
    private readonly FakeTextRecognizer _recognizer = new();

    public OcrQueueTests()
    {
        _database = ClipDatabase.InMemory($"ocr-{Guid.NewGuid():n}");
        _database.Migrate();
        _store = new SqliteClipStore(_database);
    }

    public void Dispose() => _database.Dispose();

    /// <summary>Stores a clip holding a small but genuinely decodable PNG.</summary>
    private string SaveImageClip()
    {
        string id = Guid.NewGuid().ToString("n");

        _store.Save(new StoredClip
        {
            Id = id,
            ContentHash = id,
            Title = string.Empty,
            Formats = [ClipboardFormat.Png],
            Assets = [new ClipAsset(ClipboardFormat.Png, PngBytes())],
            CreatedAt = 1000,
            UpdatedAt = 1000,
            IsConcealed = false,
            IsFromCloudClipboard = false,
        });

        return id;
    }

    private string SaveTextClip()
    {
        string id = Guid.NewGuid().ToString("n");

        _store.Save(new StoredClip
        {
            Id = id,
            ContentHash = id,
            Title = "just text",
            Formats = [ClipboardFormat.UnicodeText],
            Assets = [new ClipAsset(ClipboardFormat.UnicodeText, [104, 0, 105, 0, 0, 0])],
            CreatedAt = 1000,
            UpdatedAt = 1000,
            IsConcealed = false,
            IsFromCloudClipboard = false,
        });

        return id;
    }

    private static byte[] PngBytes()
    {
        using System.Drawing.Bitmap bitmap = new(4, 4);
        using MemoryStream stream = new();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>Polls until the clip has a recorded result, or gives up.</summary>
    private async Task<string?> WaitForOcrTextAsync(string clipId)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            if (_store.FetchOcrText(clipId) is { } text)
            {
                return text;
            }

            await Task.Delay(20);
        }

        return null;
    }

    // ---- Recognising -------------------------------------------------------------------

    [Fact]
    public async Task Recognised_text_is_stored_and_becomes_searchable()
    {
        _recognizer.Result = "INVOICE 2026-0042";
        await using OcrQueue queue = new(_recognizer, _store);
        string id = SaveImageClip();

        queue.Enqueue(id);

        Assert.Equal("INVOICE 2026-0042", await WaitForOcrTextAsync(id));
        Assert.Single(_store.Search("invoice", ClipOrder.UpdatedAt, 10));
    }

    [Fact]
    public async Task A_scan_that_finds_nothing_is_still_recorded()
    {
        // Otherwise a picture of a sunset would be rescanned on every start, forever.
        _recognizer.Result = string.Empty;
        await using OcrQueue queue = new(_recognizer, _store);
        string id = SaveImageClip();

        queue.Enqueue(id);

        Assert.Equal(string.Empty, await WaitForOcrTextAsync(id));
        Assert.Empty(_store.FetchClipsAwaitingOcr(10));
    }

    [Fact]
    public async Task A_failed_scan_is_left_for_a_retry()
    {
        // Null means the engine failed, which is different from finding no text.
        _recognizer.Result = null;
        await using OcrQueue queue = new(_recognizer, _store);
        string id = SaveImageClip();

        queue.Enqueue(id);
        Assert.True(await _recognizer.WaitForCallAsync(TimeSpan.FromSeconds(5)));
        await Task.Delay(100);

        Assert.Null(_store.FetchOcrText(id));
        Assert.Single(_store.FetchClipsAwaitingOcr(10));
    }

    [Fact]
    public async Task A_clip_with_no_image_is_marked_scanned_without_troubling_the_engine()
    {
        await using OcrQueue queue = new(_recognizer, _store);
        string id = SaveTextClip();

        queue.Enqueue(id);

        Assert.Equal(string.Empty, await WaitForOcrTextAsync(id));
        Assert.Empty(_recognizer.ReceivedImageSizes);
    }

    [Fact]
    public async Task A_clip_deleted_before_its_turn_does_not_throw()
    {
        await using OcrQueue queue = new(_recognizer, _store);
        string id = SaveImageClip();
        _store.Delete(id);

        queue.Enqueue(id);
        await Task.Delay(200);

        // Nothing to assert beyond the queue still being alive and usable.
        Assert.True(queue.IsAvailable);
    }

    [Fact]
    public async Task Very_long_text_is_capped()
    {
        // Matching the macOS 10,000-character cap: a dense page does not need to go in whole.
        _recognizer.Result = new string('x', 50_000);
        await using OcrQueue queue = new(_recognizer, _store);
        string id = SaveImageClip();

        queue.Enqueue(id);

        Assert.Equal(OcrQueue.MaximumTextLength, (await WaitForOcrTextAsync(id))!.Length);
    }

    // ---- Availability --------------------------------------------------------------------

    [Fact]
    public async Task Nothing_is_queued_when_there_is_no_OCR_engine()
    {
        _recognizer.IsAvailable = false;
        await using OcrQueue queue = new(_recognizer, _store);

        Assert.False(queue.IsAvailable);
        Assert.False(queue.Enqueue(SaveImageClip()));
        Assert.Empty(_recognizer.ReceivedImageSizes);
    }

    [Fact]
    public async Task The_backlog_is_empty_when_there_is_no_engine()
    {
        _recognizer.IsAvailable = false;
        SaveImageClip();
        await using OcrQueue queue = new(_recognizer, _store);

        Assert.Equal(0, queue.EnqueueBacklog());
    }

    // ---- Backlog ---------------------------------------------------------------------------

    [Fact]
    public async Task Unscanned_clips_are_picked_up_on_a_restart()
    {
        // Work interrupted by a close should not leave clips permanently unsearchable.
        string first = SaveImageClip();
        string second = SaveImageClip();
        _recognizer.Result = "found";

        await using OcrQueue queue = new(_recognizer, _store);
        Assert.Equal(2, queue.EnqueueBacklog());

        Assert.Equal("found", await WaitForOcrTextAsync(first));
        Assert.Equal("found", await WaitForOcrTextAsync(second));
    }

    [Fact]
    public async Task Clips_that_were_already_scanned_are_not_queued_again()
    {
        string id = SaveImageClip();
        _store.SaveOcrText(id, "already done");

        await using OcrQueue queue = new(_recognizer, _store);

        Assert.Equal(0, queue.EnqueueBacklog());
    }

    // ---- Lifetime ----------------------------------------------------------------------------

    [Fact]
    public async Task Disposing_stops_the_worker_cleanly()
    {
        OcrQueue queue = new(_recognizer, _store);
        queue.Enqueue(SaveImageClip());

        await queue.DisposeAsync();

        // A second disposal must not throw either.
        await queue.DisposeAsync();
    }

    [Fact]
    public async Task Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new OcrQueue(null!, _store));
        Assert.Throws<ArgumentNullException>(() => new OcrQueue(_recognizer, null!));

        await using OcrQueue queue = new(_recognizer, _store);
        Assert.Throws<ArgumentException>(() => queue.Enqueue(string.Empty));
    }
}
