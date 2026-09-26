namespace CopyPasta.Core.Capture;

using CopyPasta.Core.Clipboard;

/// <summary>Why a capture attempt did or did not store a clip.</summary>
public enum CaptureOutcome
{
    /// <summary>Stored.</summary>
    Captured,

    /// <summary>The clipboard has not changed since the last observed sequence number.</summary>
    Unchanged,

    /// <summary>The clipboard could not be opened; another process is holding it.</summary>
    ClipboardUnavailable,

    /// <summary>Nothing on the clipboard was worth storing.</summary>
    NothingStorable,

    /// <summary>The clip came from an excluded application.</summary>
    ExcludedApplication,

    /// <summary>The owning application failed to produce any of the chosen formats.</summary>
    NoDataProduced,

    /// <summary>Text that is empty or whitespace only.</summary>
    BlankText,

    /// <summary>Already in the history, and duplicates are not allowed.</summary>
    Duplicate,
}

/// <param name="Outcome">What happened.</param>
/// <param name="ClipId">The stored row's primary key, when one was stored.</param>
/// <param name="ContentHash">The content hash, when content was built.</param>
public sealed record CaptureResult(
    CaptureOutcome Outcome,
    string? ClipId = null,
    string? ContentHash = null)
{
    public bool WasCaptured => Outcome == CaptureOutcome.Captured;
}

/// <summary>
/// Turns a clipboard change into a stored clip, or decides not to.
/// Port of macOS <c>ClipService.create()</c> and <c>ClipService.save(_:)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The rule order is the macOS order, and it matters: the cheap rejections come first so that
/// an excluded application's 40 MB screenshot is never read off the clipboard at all.
/// </para>
/// <para>
/// Not thread-safe by assumption — it is serialised by a lock, because clipboard changes can
/// arrive faster than a capture completes and the sequence-number bookkeeping must not
/// interleave. This replaces the macOS <c>NSRecursiveLock</c>.
/// </para>
/// </remarks>
public sealed class ClipCaptureService : Paste.ICaptureSuppressor
{
    private readonly IClipboardSource _clipboard;
    private readonly IClipStore _store;
    private readonly ExcludedApplicationMatcher _excludedApplications;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();

    private uint _lastSequenceNumber;
    private bool _hasSeenSequenceNumber;

    public ClipCaptureService(
        IClipboardSource clipboard,
        IClipStore store,
        IForegroundApplication foregroundApplication,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(clipboard);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(foregroundApplication);

        _clipboard = clipboard;
        _store = store;
        _excludedApplications = new ExcludedApplicationMatcher(foregroundApplication);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Settings in force for the next capture.</summary>
    public CaptureSettings Settings { get; set; } = CaptureSettings.Default;

    /// <summary>
    /// Treats the clipboard's current state as already seen, so the next change notification for
    /// it is ignored.
    /// </summary>
    /// <remarks>
    /// This is how the app avoids re-capturing its own writes: the paste path calls this
    /// immediately after putting a clip back on the clipboard. Port of the macOS
    /// <c>incrementChangeCount()</c> trick, but exact rather than approximate — Windows gives us
    /// the real sequence number instead of a counter we have to guess at.
    /// </remarks>
    public void IgnoreCurrentClipboardState()
    {
        lock (_gate)
        {
            _lastSequenceNumber = _clipboard.GetSequenceNumber();
            _hasSeenSequenceNumber = true;
        }
    }

    /// <summary>Attempts to capture whatever is currently on the clipboard.</summary>
    public CaptureResult Capture()
    {
        lock (_gate)
        {
            uint sequenceNumber = _clipboard.GetSequenceNumber();
            if (_hasSeenSequenceNumber && sequenceNumber == _lastSequenceNumber)
            {
                return new CaptureResult(CaptureOutcome.Unchanged);
            }

            CaptureOutcome? rejection = null;
            ClipboardSelection selection = ClipboardSelection.None;

            ClipboardRead? read = _clipboard.TryRead(snapshot =>
            {
                selection = ClipboardFormatFilter.Select(snapshot, Settings.Filter);
                if (selection.IsEmpty)
                {
                    rejection = CaptureOutcome.NothingStorable;
                    return [];
                }

                // Checked before the blobs are fetched, unlike macOS, which has already
                // materialised them by this point. Same decision, less wasted I/O.
                if (_excludedApplications.IsExcluded(
                        Settings.ExcludedApplications,
                        snapshot.Formats))
                {
                    rejection = CaptureOutcome.ExcludedApplication;
                    return [];
                }

                return selection.Formats;
            });

            if (read is null)
            {
                // Do not record the sequence number: the clip has not been examined, so a
                // retry on the next notification should still consider it.
                return new CaptureResult(CaptureOutcome.ClipboardUnavailable);
            }

            _lastSequenceNumber = read.SequenceNumber;
            _hasSeenSequenceNumber = true;

            if (rejection is not null)
            {
                return new CaptureResult(rejection.Value);
            }

            if (!ClipContent.TryCreate(selection, read.Assets, out ClipContent? content))
            {
                // Every chosen format failed to render. Delayed rendering means the owning app
                // produces the bytes on demand, and it is allowed to fail.
                return new CaptureResult(CaptureOutcome.NoDataProduced);
            }

            if (content.IsBlankText)
            {
                return new CaptureResult(CaptureOutcome.BlankText, ContentHash: content.Hash);
            }

            return Store(content, selection);
        }
    }

    /// <summary>
    /// Captures an image that never went through the clipboard, such as a saved screenshot.
    /// Port of the macOS <c>create(with image:)</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately skips the sequence-number check and the excluded-application check. Neither
    /// applies: nothing was copied, and the foreground application at the moment a screenshot lands
    /// on disk has nothing to do with where the image came from.
    /// </remarks>
    public CaptureResult CaptureImage(ClipboardFormat format, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length == 0)
        {
            return new CaptureResult(CaptureOutcome.NoDataProduced);
        }

        lock (_gate)
        {
            if (!Settings.Filter.EnabledTypes.Contains(ClipContentType.Image))
            {
                return new CaptureResult(CaptureOutcome.NothingStorable);
            }

            if (!ClipContent.TryCreate([new ClipAsset(format, data)], out ClipContent? content))
            {
                return new CaptureResult(CaptureOutcome.NoDataProduced);
            }

            return Store(content, ClipboardSelection.None);
        }
    }

    private CaptureResult Store(ClipContent content, ClipboardSelection selection)
    {
        if (!Settings.AllowsDuplicates && _store.Exists(content.Hash))
        {
            return new CaptureResult(CaptureOutcome.Duplicate, ContentHash: content.Hash);
        }

        // The id decides the duplicate behaviour: reusing the content hash makes Save an
        // upsert that lifts the existing entry, while a fresh GUID gives every copy its own row.
        string clipId = Settings.OverwritesDuplicates
            ? content.Hash
            : Guid.NewGuid().ToString("n");

        long now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();

        _store.Save(new StoredClip
        {
            Id = clipId,
            ContentHash = content.Hash,
            Title = BuildTitle(content),
            Formats = content.Formats,
            Assets = content.Assets,
            CreatedAt = now,
            UpdatedAt = now,
            IsConcealed = selection.IsConcealed,
            IsFromCloudClipboard = selection.IsFromCloudClipboard,
        });

        return new CaptureResult(CaptureOutcome.Captured, clipId, content.Hash);
    }

    private string BuildTitle(ClipContent content)
    {
        string? text = content.TextValue;
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= Settings.MaximumTitleLength
            ? text
            : text[..Settings.MaximumTitleLength];
    }
}
