using System.Runtime.InteropServices;
using CopyPasta.Core.Capture;
using CopyPasta.Core.Clipboard;

namespace CopyPasta.Interop;

/// <summary>Reads the Windows clipboard.</summary>
/// <remarks>
/// <para>
/// The awkward parts of the Win32 contract live here and nowhere else:
/// </para>
/// <list type="bullet">
/// <item><b>Contention.</b> <c>OpenClipboard</c> fails outright if another process holds the
/// clipboard, which happens routinely — Office, browsers and RDP all hold it briefly while
/// building their offerings. Every read retries with backoff and gives up quietly.</item>
/// <item><b>Delayed rendering.</b> An owning app can advertise a format and only produce the
/// bytes when asked, and it is allowed to fail. A missing blob is normal, not an error.</item>
/// <item><b>Handle formats.</b> <c>CF_BITMAP</c> and friends hand back a GDI handle rather than
/// bytes; passing one to <c>GlobalLock</c> is undefined. They are never read.</item>
/// </list>
/// </remarks>
public sealed class Win32ClipboardSource : IClipboardSource
{
    private const int MaximumOpenAttempts = 12;
    private const int InitialBackoffMilliseconds = 8;
    private const int MaximumBackoffMilliseconds = 120;

    private readonly ClipboardFormatRegistry _formats;
    private readonly IntPtr _ownerWindow;

    public Win32ClipboardSource(ClipboardFormatRegistry formats, IntPtr ownerWindow = default)
    {
        ArgumentNullException.ThrowIfNull(formats);
        _formats = formats;
        _ownerWindow = ownerWindow;
    }

    public uint GetSequenceNumber() => NativeMethods.GetClipboardSequenceNumber();

    public ClipboardRead? TryRead(ClipboardFormatChooser chooser)
    {
        ArgumentNullException.ThrowIfNull(chooser);

        if (!TryOpen())
        {
            return null;
        }

        try
        {
            OfferedFormats offered = EnumerateFormats();
            Dictionary<ClipboardFormat, uint> idsByFormat = offered.IdsByFormat;
            ClipboardSnapshot snapshot = BuildSnapshot(offered);

            IReadOnlyList<ClipboardFormat> wanted = chooser(snapshot);

            List<ClipAsset> assets = new(wanted.Count);
            foreach (ClipboardFormat format in wanted)
            {
                if (!idsByFormat.TryGetValue(format, out uint id))
                {
                    continue;
                }

                if (TryReadBlob(id, format, out byte[]? data))
                {
                    assets.Add(new ClipAsset(format, data));
                }
            }

            return new ClipboardRead
            {
                Snapshot = snapshot,
                Assets = assets,
                SequenceNumber = NativeMethods.GetClipboardSequenceNumber(),
            };
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    private bool TryOpen()
    {
        int backoff = InitialBackoffMilliseconds;

        for (int attempt = 0; attempt < MaximumOpenAttempts; attempt++)
        {
            if (NativeMethods.OpenClipboard(_ownerWindow))
            {
                return true;
            }

            Thread.Sleep(backoff);
            backoff = Math.Min(backoff * 2, MaximumBackoffMilliseconds);
        }

        return false;
    }

    /// <param name="Ordered">
    /// Formats in the order the clipboard reports them, which is the offering app's own
    /// priority order. Order is significant to the filter, so it is kept separately from the
    /// lookup dictionary.
    /// </param>
    /// <param name="IdsByFormat">Numeric id per format; duplicates keep the first id seen.</param>
    private readonly record struct OfferedFormats(
        IReadOnlyList<ClipboardFormat> Ordered,
        Dictionary<ClipboardFormat, uint> IdsByFormat);

    private OfferedFormats EnumerateFormats()
    {
        Dictionary<ClipboardFormat, uint> idsByFormat = [];
        List<ClipboardFormat> ordered = [];

        uint id = 0;
        while ((id = NativeMethods.EnumClipboardFormats(id)) != 0)
        {
            ClipboardFormat format = _formats.Resolve(id);
            if (idsByFormat.TryAdd(format, id))
            {
                ordered.Add(format);
            }
        }

        return new OfferedFormats(ordered, idsByFormat);
    }

    private static ClipboardSnapshot BuildSnapshot(OfferedFormats offered)
    {
        Dictionary<ClipboardFormat, uint> idsByFormat = offered.IdsByFormat;

        return new ClipboardSnapshot
        {
            Formats = offered.Ordered,
            CanIncludeInClipboardHistory =
                ReadMarkerFlag(idsByFormat, ClipboardFormat.CanIncludeInClipboardHistory),
            CanUploadToCloudClipboard =
                ReadMarkerFlag(idsByFormat, ClipboardFormat.CanUploadToCloudClipboard),
            IsFromCloudClipboard =
                idsByFormat.ContainsKey(ClipboardFormat.FromCloudClipboard),
        };
    }

    /// <summary>
    /// Reads one of the DWORD marker formats. Absent means "the app said nothing", which is
    /// materially different from an explicit zero.
    /// </summary>
    private static bool? ReadMarkerFlag(
        Dictionary<ClipboardFormat, uint> idsByFormat,
        ClipboardFormat marker)
    {
        if (!idsByFormat.TryGetValue(marker, out uint id))
        {
            return null;
        }

        if (!TryReadBlob(id, marker, out byte[]? data) || data.Length < sizeof(uint))
        {
            // Present but unreadable or malformed. Treat presence alone as the conservative
            // signal: the app went out of its way to mark the clip.
            return false;
        }

        return BitConverter.ToUInt32(data, 0) != 0;
    }

    private static bool TryReadBlob(
        uint id,
        ClipboardFormat format,
        out byte[] data)
    {
        data = [];

        if (NativeMethods.IsHandleBasedFormat(id))
        {
            return false;
        }

        IntPtr handle = NativeMethods.GetClipboardData(id);
        if (handle == IntPtr.Zero)
        {
            // Delayed rendering: the owner declined or failed to produce the bytes.
            return false;
        }

        nuint allocatedSize = NativeMethods.GlobalSize(handle);
        if (allocatedSize == 0)
        {
            return false;
        }

        IntPtr pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            int length = checked((int)allocatedSize);
            byte[] buffer = new byte[length];
            Marshal.Copy(pointer, buffer, 0, length);

            int logicalLength = LogicalLength(format, buffer);
            data = logicalLength == length ? buffer : buffer[..logicalLength];
            return data.Length > 0;
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }
    }

    /// <summary>
    /// The meaningful length of a blob, trimming the slack that <c>GlobalAlloc</c> may leave
    /// past the data.
    /// </summary>
    /// <remarks>
    /// This is not re-encoding; it is reading the correct length. It matters for correctness,
    /// not tidiness: <c>GlobalSize</c> reports the size of the allocated block, which can be
    /// larger than what the app actually wrote. Since the blob's bytes feed the content hash,
    /// uncompensated slack would make two copies of identical text hash differently and defeat
    /// duplicate detection. Only the NUL-terminated text formats have a discoverable logical
    /// length; every other format is stored exactly as offered.
    /// </remarks>
    private static int LogicalLength(ClipboardFormat format, byte[] buffer)
    {
        if (format == ClipboardFormat.UnicodeText)
        {
            for (int offset = 0; offset + 1 < buffer.Length; offset += 2)
            {
                if (buffer[offset] == 0 && buffer[offset + 1] == 0)
                {
                    return offset + 2;
                }
            }

            return buffer.Length;
        }

        if (format == ClipboardFormat.Text || format == ClipboardFormat.OemText)
        {
            int terminator = Array.IndexOf(buffer, (byte)0);
            return terminator >= 0 ? terminator + 1 : buffer.Length;
        }

        return buffer.Length;
    }
}
