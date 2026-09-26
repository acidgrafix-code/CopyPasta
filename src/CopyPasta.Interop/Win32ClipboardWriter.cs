using System.Runtime.InteropServices;
using CopyPasta.Core.Clipboard;
using CopyPasta.Core.Paste;

namespace CopyPasta.Interop;

/// <summary>Writes content to the Windows clipboard.</summary>
/// <remarks>
/// <para>
/// Ownership is the thing to get right: once <c>SetClipboardData</c> succeeds, the system owns
/// the <c>HGLOBAL</c> and freeing it would corrupt the clipboard. Only a handle that was never
/// handed over gets freed here.
/// </para>
/// <para>
/// Every format is written byte for byte as captured. Nothing is re-encoded, synthesised or
/// normalised — Windows adds its own synthesised flavours (CF_TEXT from CF_UNICODETEXT, CF_DIB
/// from CF_DIBV5) at no cost to us.
/// </para>
/// </remarks>
public sealed class Win32ClipboardWriter : IClipboardWriter
{
    private const int MaximumOpenAttempts = 12;
    private const int InitialBackoffMilliseconds = 8;
    private const int MaximumBackoffMilliseconds = 120;

    private readonly ClipboardFormatRegistry _formats;
    private readonly IntPtr _ownerWindow;

    public Win32ClipboardWriter(ClipboardFormatRegistry formats, IntPtr ownerWindow = default)
    {
        ArgumentNullException.ThrowIfNull(formats);
        _formats = formats;
        _ownerWindow = ownerWindow;
    }

    public bool TryWrite(ClipboardWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryOpen())
        {
            return false;
        }

        try
        {
            if (!NativeMethods.EmptyClipboard())
            {
                return false;
            }

            foreach (ClipAsset asset in request.Assets)
            {
                uint id = _formats.ResolveId(asset.Format);
                if (id == 0)
                {
                    continue;
                }

                TrySetData(id, asset.Data);
            }

            if (request.MarkAsConcealed)
            {
                MarkConcealed();
            }

            return true;
        }
        finally
        {
            NativeMethods.CloseClipboard();
        }
    }

    /// <summary>
    /// Re-applies the sensitive-content markers, so replaying a password does not leak it into
    /// Windows' own clipboard history or sync it to other devices.
    /// </summary>
    /// <remarks>
    /// This is the Windows counterpart of macOS re-writing <c>org.nspasteboard.ConcealedType</c>.
    /// Both DWORDs are set to zero: a clip the source app considered too sensitive for history is
    /// not one we should upload to the cloud either.
    /// </remarks>
    private void MarkConcealed()
    {
        WriteMarkerFlag(ClipboardFormat.CanIncludeInClipboardHistory, value: 0);
        WriteMarkerFlag(ClipboardFormat.CanUploadToCloudClipboard, value: 0);
    }

    private void WriteMarkerFlag(ClipboardFormat marker, uint value)
    {
        uint id = _formats.ResolveId(marker);
        if (id != 0)
        {
            TrySetData(id, BitConverter.GetBytes(value));
        }
    }

    private static bool TrySetData(uint formatId, byte[] data)
    {
        IntPtr handle = AllocateGlobal(data);
        if (handle == IntPtr.Zero)
        {
            return false;
        }

        if (NativeMethods.SetClipboardData(formatId, handle) != IntPtr.Zero)
        {
            // The clipboard owns the handle now; freeing it here would corrupt the clipboard.
            return true;
        }

        NativeMethods.GlobalFree(handle);
        return false;
    }

    private static IntPtr AllocateGlobal(byte[] data)
    {
        // GMEM_MOVEABLE is required: the clipboard rejects fixed memory.
        // A zero-length allocation is not valid, so empty data gets one padding byte.
        nuint size = (nuint)Math.Max(data.Length, 1);
        IntPtr handle = NativeMethods.GlobalAlloc(NativeMethods.GMEM_MOVEABLE, size);
        if (handle == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr pointer = NativeMethods.GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            NativeMethods.GlobalFree(handle);
            return IntPtr.Zero;
        }

        try
        {
            if (data.Length > 0)
            {
                Marshal.Copy(data, 0, pointer, data.Length);
            }
            else
            {
                Marshal.WriteByte(pointer, 0);
            }
        }
        finally
        {
            NativeMethods.GlobalUnlock(handle);
        }

        return handle;
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
}
