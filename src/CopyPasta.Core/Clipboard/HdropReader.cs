using System.Buffers.Binary;
using System.Text;

namespace CopyPasta.Core.Clipboard;

/// <summary>
/// Parses the file paths out of a CF_HDROP blob.
/// </summary>
/// <remarks>
/// <para>
/// Replaces the macOS <c>fileURL</c> handling. The blob is a <c>DROPFILES</c> struct followed
/// by a double-NUL-terminated list of paths:
/// </para>
/// <code>
/// offset  size  field
/// 0       4     pFiles  — byte offset from the start of the blob to the path list
/// 4       8     pt      — POINT, unused here
/// 12      4     fNC     — BOOL, unused here
/// 16      4     fWide   — BOOL: non-zero means UTF-16LE paths, zero means ANSI
/// pFiles  ...   "a\0b\0\0"
/// </code>
/// <para>
/// Pure byte parsing, so it belongs in Core and is directly testable. It is tolerant of
/// malformed input: anything it cannot make sense of yields an empty list rather than
/// throwing, because this runs against blobs written by arbitrary third-party apps.
/// </para>
/// </remarks>
public static class HdropReader
{
    private const int PFilesOffset = 0;
    private const int FWideOffset = 16;
    private const int HeaderSize = 20;

    public static IReadOnlyList<string> ReadPaths(ReadOnlySpan<byte> blob)
    {
        if (blob.Length < HeaderSize)
        {
            return [];
        }

        uint listOffset = BinaryPrimitives.ReadUInt32LittleEndian(blob[PFilesOffset..]);
        bool isWide = BinaryPrimitives.ReadInt32LittleEndian(blob[FWideOffset..]) != 0;

        if (listOffset < HeaderSize || listOffset >= (uint)blob.Length)
        {
            return [];
        }

        ReadOnlySpan<byte> list = blob[(int)listOffset..];
        string decoded = isWide
            ? DecodeWide(list)
            : Encoding.Latin1.GetString(list);

        List<string> paths = [];
        foreach (string candidate in decoded.Split('\0'))
        {
            if (candidate.Length == 0)
            {
                // The first empty entry marks the terminating double NUL.
                break;
            }

            paths.Add(candidate);
        }

        return paths;
    }

    private static string DecodeWide(ReadOnlySpan<byte> list)
    {
        // Truncate a trailing odd byte rather than letting the decoder produce a
        // replacement character mid-path.
        int usable = list.Length - (list.Length % 2);
        return Encoding.Unicode.GetString(list[..usable]);
    }
}
