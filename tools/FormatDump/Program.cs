using System.Runtime.InteropServices;
using System.Text;

namespace FormatDump;

/// <summary>
/// Prints what the clipboard is currently offering, so the hand-authored expectations in
/// <c>CaptureScenarioTests</c> can be replaced with real captures.
/// </summary>
/// <remarks>
/// Deliberately tiny and standalone: it is a measurement instrument, not part of the app.
/// The real interop layer (Phase 1) needs retry-with-backoff, delayed rendering and a
/// message-only window; none of that belongs here.
///
///   Usage:  dotnet run --project tools/FormatDump            once, now
///           dotnet run --project tools/FormatDump -- --watch until Ctrl+C
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        bool watch = args.Contains("--watch", StringComparer.OrdinalIgnoreCase);

        if (!watch)
        {
            return Dump() ? 0 : 1;
        }

        Console.WriteLine("Watching the clipboard. Copy something, or press Ctrl+C to stop.");
        uint lastSequence = 0;
        while (true)
        {
            uint sequence = GetClipboardSequenceNumber();
            if (sequence != lastSequence)
            {
                lastSequence = sequence;
                Console.WriteLine();
                Console.WriteLine($"--- sequence {sequence} ---");
                Dump();
            }

            Thread.Sleep(250);
        }
    }

    private static bool Dump()
    {
        // The real capture path must retry: another process can hold the clipboard open.
        // A few attempts is plenty for a diagnostic.
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    PrintFormats();
                    return true;
                }
                finally
                {
                    CloseClipboard();
                }
            }

            Thread.Sleep(50);
        }

        Console.Error.WriteLine("Could not open the clipboard; another process is holding it.");
        return false;
    }

    private static void PrintFormats()
    {
        Console.WriteLine($"Owner: {DescribeOwner()}");
        Console.WriteLine("Formats, in the order the clipboard reports them:");

        uint format = 0;
        int index = 0;
        StringBuilder name = new(256);

        while ((format = EnumClipboardFormats(format)) != 0)
        {
            string label = StandardName(format) ?? RegisteredName(format, name) ?? "(unnamed)";
            int size = SizeOf(format);
            Console.WriteLine($"  [{index,2}] {format,-6} {label,-48} {size,12:N0} bytes");
            index++;
        }

        if (index == 0)
        {
            Console.WriteLine("  (clipboard is empty)");
        }
    }

    private static string DescribeOwner()
    {
        IntPtr owner = GetClipboardOwner();
        if (owner == IntPtr.Zero)
        {
            return "unknown";
        }

        _ = GetWindowThreadProcessId(owner, out uint processId);
        try
        {
            return $"{System.Diagnostics.Process.GetProcessById((int)processId).ProcessName} (pid {processId})";
        }
        catch (ArgumentException)
        {
            return $"pid {processId}";
        }
    }

    private static int SizeOf(uint format)
    {
        IntPtr handle = GetClipboardData(format);
        if (handle == IntPtr.Zero)
        {
            return 0;
        }

        // GlobalSize is meaningless for handle-based formats such as CF_BITMAP; report 0
        // rather than a misleading number.
        return format is CF_BITMAP or CF_PALETTE or CF_METAFILEPICT or CF_ENHMETAFILE
            ? 0
            : (int)GlobalSize(handle);
    }

    private static string? RegisteredName(uint format, StringBuilder buffer)
    {
        buffer.Clear();
        int length = GetClipboardFormatName(format, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : null;
    }

    private const uint CF_BITMAP = 2;
    private const uint CF_METAFILEPICT = 3;
    private const uint CF_PALETTE = 9;
    private const uint CF_ENHMETAFILE = 14;

    private static string? StandardName(uint format) => format switch
    {
        1 => "CF_TEXT",
        2 => "CF_BITMAP",
        3 => "CF_METAFILEPICT",
        4 => "CF_SYLK",
        5 => "CF_DIF",
        6 => "CF_TIFF",
        7 => "CF_OEMTEXT",
        8 => "CF_DIB",
        9 => "CF_PALETTE",
        10 => "CF_PENDATA",
        11 => "CF_RIFF",
        12 => "CF_WAVE",
        13 => "CF_UNICODETEXT",
        14 => "CF_ENHMETAFILE",
        15 => "CF_HDROP",
        16 => "CF_LOCALE",
        17 => "CF_DIBV5",
        _ => null,
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint EnumClipboardFormats(uint format);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetClipboardFormatName(uint format, StringBuilder lpszFormatName, int cchMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr GlobalSize(IntPtr hMem);
}
