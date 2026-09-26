using System.Text;
using CopyPasta.Core.Capture;
using CopyPasta.Core.Paste;

namespace CopyPasta.Core.Tests;

/// <summary>A scripted clipboard, so the capture pipeline can be tested without Windows.</summary>
public sealed class FakeClipboard : IClipboardSource
{
    private ClipboardSnapshot _snapshot = ClipboardSnapshot.FromFormats();
    private Dictionary<ClipboardFormat, byte[]> _blobs = [];

    public uint SequenceNumber { get; set; } = 1;

    /// <summary>Formats the chooser asked for on the last read.</summary>
    public IReadOnlyList<ClipboardFormat> LastRequestedFormats { get; private set; } = [];

    public int ReadAttempts { get; private set; }

    /// <summary>Simulates another process holding the clipboard.</summary>
    public bool IsUnavailable { get; set; }

    /// <summary>
    /// Formats that advertise but refuse to render, simulating delayed rendering failure.
    /// </summary>
    public HashSet<ClipboardFormat> FailingFormats { get; } = [];

    public FakeClipboard Put(ClipboardSnapshot snapshot, Dictionary<ClipboardFormat, byte[]> blobs)
    {
        _snapshot = snapshot;
        _blobs = blobs;
        SequenceNumber++;
        return this;
    }

    public FakeClipboard PutText(string text)
    {
        return Put(
            ClipboardSnapshot.FromFormats(ClipboardFormat.UnicodeText),
            new Dictionary<ClipboardFormat, byte[]>
            {
                [ClipboardFormat.UnicodeText] = Encoding.Unicode.GetBytes(text + '\0'),
            });
    }

    public FakeClipboard PutFormats(params ClipboardFormat[] formats)
    {
        Dictionary<ClipboardFormat, byte[]> blobs = [];
        for (int index = 0; index < formats.Length; index++)
        {
            blobs[formats[index]] = [(byte)(index + 1)];
        }

        return Put(new ClipboardSnapshot { Formats = formats }, blobs);
    }

    public uint GetSequenceNumber() => SequenceNumber;

    public ClipboardRead? TryRead(ClipboardFormatChooser chooser)
    {
        ReadAttempts++;

        if (IsUnavailable)
        {
            return null;
        }

        IReadOnlyList<ClipboardFormat> wanted = chooser(_snapshot);
        LastRequestedFormats = wanted;

        List<ClipAsset> assets = [];
        foreach (ClipboardFormat format in wanted)
        {
            if (FailingFormats.Contains(format))
            {
                continue;
            }

            if (_blobs.TryGetValue(format, out byte[]? data))
            {
                assets.Add(new ClipAsset(format, data));
            }
        }

        return new ClipboardRead
        {
            Snapshot = _snapshot,
            Assets = assets,
            SequenceNumber = SequenceNumber,
        };
    }
}

/// <summary>An in-memory clip store.</summary>
public sealed class FakeClipStore : IClipStore
{
    private readonly Dictionary<string, StoredClip> _clips = [];

    public IReadOnlyCollection<StoredClip> Clips => _clips.Values;

    public StoredClip? Single => _clips.Count == 1 ? _clips.Values.First() : null;

    public bool Exists(string clipId) => _clips.ContainsKey(clipId);

    public void Save(StoredClip clip) => _clips[clip.Id] = clip;

    public int Count() => _clips.Count;

    public bool Delete(string clipId) => _clips.Remove(clipId);

    public bool Touch(string clipId, long updatedAt)
    {
        if (!_clips.TryGetValue(clipId, out StoredClip? clip))
        {
            return false;
        }

        _clips[clipId] = clip with { UpdatedAt = updatedAt };
        return true;
    }

    public void Add(StoredClip clip) => _clips[clip.Id] = clip;
}

/// <summary>A clipboard that records what was written to it.</summary>
public sealed class FakeClipboardWriter : IClipboardWriter
{
    public List<ClipboardWriteRequest> Writes { get; } = [];

    public bool IsUnavailable { get; set; }

    public ClipboardWriteRequest? LastWrite => Writes.Count > 0 ? Writes[^1] : null;

    public bool TryWrite(ClipboardWriteRequest request)
    {
        if (IsUnavailable)
        {
            return false;
        }

        Writes.Add(request);
        return true;
    }
}

/// <summary>Records paste keystrokes without sending any.</summary>
public sealed class FakeInputSender : IInputSender
{
    public int SendCount { get; private set; }

    public bool Succeeds { get; set; } = true;

    public bool TrySendPasteShortcut()
    {
        SendCount++;
        return Succeeds;
    }
}

/// <summary>A scripted focus owner.</summary>
public sealed class FakeWindowFocus : IWindowFocus
{
    public FocusToken Captured { get; set; } = new(1234);

    public bool RestoreSucceeds { get; set; } = true;

    public List<FocusToken> RestoreAttempts { get; } = [];

    public FocusToken Capture() => Captured;

    public bool TryRestore(FocusToken token)
    {
        RestoreAttempts.Add(token);
        return RestoreSucceeds;
    }
}

/// <summary>Modifier keys the test says are held.</summary>
public sealed class FakeModifierKeys : IModifierKeys
{
    public HashSet<ModifierKey> Held { get; } = [];

    public static FakeModifierKeys Holding(params ModifierKey[] keys)
    {
        FakeModifierKeys keyboard = new();
        foreach (ModifierKey key in keys)
        {
            keyboard.Held.Add(key);
        }

        return keyboard;
    }

    public bool IsPressed(ModifierKey key) => Held.Contains(key);
}

/// <summary>Counts capture-suppression requests.</summary>
public sealed class FakeCaptureSuppressor : ICaptureSuppressor
{
    public int SuppressCount { get; private set; }

    public void IgnoreCurrentClipboardState() => SuppressCount++;
}

/// <summary>A fixed foreground application.</summary>
public sealed class FakeForegroundApplication : IForegroundApplication
{
    public ForegroundApplicationInfo? Current { get; set; }

    public static FakeForegroundApplication Named(string processName, string? path = null) =>
        new() { Current = new ForegroundApplicationInfo(processName, path) };

    public ForegroundApplicationInfo? GetCurrent() => Current;
}
