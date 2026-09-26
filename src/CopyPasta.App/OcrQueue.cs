using System.Threading.Channels;
using CopyPasta.Core.Clipboard;
using CopyPasta.Core.Ocr;
using CopyPasta.Data;

namespace CopyPasta.App;

/// <summary>
/// Runs text recognition in the background, one image at a time.
/// </summary>
/// <remarks>
/// <para>
/// Port of the macOS <c>TextRecognizer</c>, which uses a serial utility-priority dispatch queue.
/// Serial matters for the same reason there: recognition on a full-screen image is measured in
/// hundreds of milliseconds, and letting several run at once would turn a burst of screenshots into
/// a CPU spike on the user's machine while they are trying to work.
/// </para>
/// <para>
/// Off the message-loop thread entirely. The capture path runs inside a clipboard notification,
/// and the clipboard is a shared resource other applications are waiting on.
/// </para>
/// </remarks>
public sealed class OcrQueue : IAsyncDisposable
{
    /// <summary>
    /// Characters of recognised text kept, matching the macOS cap. A dense page of text can run to
    /// tens of thousands of characters, and the index does not need all of it.
    /// </summary>
    public const int MaximumTextLength = 10_000;

    private readonly IImageTextRecognizer _recognizer;
    private readonly SqliteClipStore _store;
    private readonly Channel<string> _queue;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private bool _disposed;

    public OcrQueue(IImageTextRecognizer recognizer, SqliteClipStore store)
    {
        ArgumentNullException.ThrowIfNull(recognizer);
        ArgumentNullException.ThrowIfNull(store);

        _recognizer = recognizer;
        _store = store;

        // Bounded, dropping the oldest: a backlog of screenshots is not worth unbounded memory,
        // and the newest are the ones the user is most likely to search for.
        _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        _worker = Task.Run(ProcessAsync);
    }

    /// <summary>Raised for anything worth logging.</summary>
    public event EventHandler<string>? Trace;

    public bool IsAvailable => _recognizer.IsAvailable;

    /// <summary>Queues a clip for recognition. Returns false when it was not accepted.</summary>
    public bool Enqueue(string clipId)
    {
        ArgumentException.ThrowIfNullOrEmpty(clipId);

        return _recognizer.IsAvailable && _queue.Writer.TryWrite(clipId);
    }

    /// <summary>
    /// Queues clips that were captured but never scanned, so a restart picks up interrupted work.
    /// </summary>
    public int EnqueueBacklog(int limit = 32)
    {
        if (!_recognizer.IsAvailable)
        {
            return 0;
        }

        int queued = 0;

        foreach (string clipId in _store.FetchClipsAwaitingOcr(limit))
        {
            if (Enqueue(clipId))
            {
                queued++;
            }
        }

        return queued;
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (string clipId in _queue.Reader.ReadAllAsync(_shutdown.Token)
                               .ConfigureAwait(false))
            {
                await RecognizeAsync(clipId).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task RecognizeAsync(string clipId)
    {
        try
        {
            ClipContent? content = _store.FetchContent(clipId);

            if (content is null || !content.TryGetDecodableImage(out byte[]? image))
            {
                // Not an image, or the clip is gone. Record an empty result so it is not
                // reconsidered on every start.
                _store.SaveOcrText(clipId, string.Empty);
                return;
            }

            string? text = await _recognizer
                .RecognizeAsync(image, _shutdown.Token)
                .ConfigureAwait(false);

            if (text is null)
            {
                // A failure, not an empty page. Left unrecorded so it can be retried.
                Trace?.Invoke(this, $"could not recognise text in {clipId[..12]}");
                return;
            }

            string trimmed = text.Length > MaximumTextLength ? text[..MaximumTextLength] : text;
            _store.SaveOcrText(clipId, trimmed);

            if (trimmed.Length > 0)
            {
                Trace?.Invoke(this, $"recognised {trimmed.Length} characters in {clipId[..12]}");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Recognition is a nicety; never let it take the app down.
            Trace?.Invoke(this, $"text recognition failed for {clipId[..12]}: {exception.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        // DisposeAsync is required to be safe to call more than once, and without this the second
        // call cancels an already-disposed token source and throws.
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _queue.Writer.TryComplete();
        await _shutdown.CancelAsync().ConfigureAwait(false);

        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected.
        }

        _shutdown.Dispose();
    }
}
