using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using CopyPasta.Core.Capture;
using CopyPasta.Core.Clipboard;
using CopyPasta.Core.Paste;
using CopyPasta.Interop;

namespace CaptureProbe;

/// <summary>
/// Drives the whole paste path into a real Win32 edit control and checks what arrived.
/// </summary>
/// <remarks>
/// <para>
/// The Phase 2 acceptance check for the half that <c>--roundtrip</c> cannot cover: clipboard
/// write, foreground-window restore, and synthesised Ctrl+V actually landing in a target that
/// processes it.
/// </para>
/// <para>
/// The target is an edit control this process owns, for two reasons. Windows 11's Notepad is a
/// packaged app whose text is not reachable through <c>WM_GETTEXT</c>, and a window we own can be
/// read back deterministically. <c>SendInput</c> goes through the system input queue to whatever
/// holds the foreground regardless of which process owns it, so the mechanism under test is the
/// real one; what this cannot exercise is UIPI, since that needs a target at a higher integrity
/// level (see InputSender's remarks).
/// </para>
/// </remarks>
internal static class PasteTest
{
    private const string HostClassName = "CopyPasta.PasteTestHost";
    private const int IdEdit = 1001;

    public static bool Run(ClipboardFormatRegistry formats, bool holdShift = false)
    {
        const string expected = "paste path verification — café 日本語 42";

        using Host host = Host.Create();
        if (!host.TryBringToForeground())
        {
            Console.Error.WriteLine("Could not bring the test window to the foreground.");
            return false;
        }

        Win32ClipboardWriter writer = new(formats);
        StubStore store = new();

        PasteService paste = new(
            writer,
            new InputSender(),
            new WindowFocus(),
            new ModifierKeys(),
            store,
            store);

        ClipContent content = BuildTextClip(expected);
        store.Add("test-clip", content);

        // Holding a modifier is how the plain-text and delete actions are triggered, so the key
        // is still physically down when the paste is synthesised. Without neutralising it the
        // target receives Ctrl+Shift+V, which an edit control ignores entirely — so if the text
        // arrives with Shift held, the neutralisation works.
        if (holdShift)
        {
            Console.WriteLine("Holding Shift while pasting.");
            keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
        }

        PasteResult result = paste.Paste(new PasteRequest
        {
            ClipId = "test-clip",
            Content = content,
            Target = new FocusToken((long)host.Window),
        });

        if (holdShift)
        {
            keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        Console.WriteLine($"Paste outcome : {result.Outcome}");

        // The edit control has to process the synthesised keystrokes, and it lives on this
        // thread, so this thread must pump.
        host.PumpFor(TimeSpan.FromMilliseconds(800));

        string actual = host.ReadEditText();
        Console.WriteLine($"Expected      : {expected}");
        Console.WriteLine($"In the target : {actual}");

        bool matched = actual == expected;
        Console.WriteLine();
        Console.WriteLine(matched
            ? "ok    the synthesised paste delivered the exact clipboard content"
            : "FAIL  the target did not receive the expected text");

        if (matched && store.Touched.Count == 1)
        {
            Console.WriteLine("ok    the clip was lifted in the last-used ordering");
        }

        if (holdShift)
        {
            // Control experiment: does the neutralisation actually matter for this target, or
            // would a naive Ctrl+Shift+V have worked anyway? Without this the passing test above
            // would prove nothing.
            host.ClearEdit();
            host.PumpFor(TimeSpan.FromMilliseconds(100));

            keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            host.PumpFor(TimeSpan.FromMilliseconds(600));
            string naive = host.ReadEditText();
            Console.WriteLine();
            string verdict = naive == expected
                ? "the text anyway, so this target tolerates the stray modifier"
                : $"nothing useful ({naive.Length} chars), so the neutralisation is load-bearing";
            Console.WriteLine($"Control: naive Ctrl+Shift+V delivered {verdict}");
        }

        return matched;
    }

    private const byte VK_SHIFT = 0x10;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private static ClipContent BuildTextClip(string text)
    {
        ClipAsset asset = new(
            ClipboardFormat.UnicodeText,
            Encoding.Unicode.GetBytes(text + '\0'));

        if (!ClipContent.TryCreate([asset], out ClipContent? content))
        {
            throw new InvalidOperationException("Could not build the test clip.");
        }

        return content;
    }

    /// <summary>A throwaway store that records what the paste path did to it.</summary>
    private sealed class StubStore : IClipStore, ICaptureSuppressor
    {
        private readonly Dictionary<string, ClipContent> _clips = [];

        public List<string> Touched { get; } = [];

        public void Add(string id, ClipContent content) => _clips[id] = content;

        public bool Exists(string clipId) => _clips.ContainsKey(clipId);

        public void Save(StoredClip clip) { }

        public int Count() => _clips.Count;

        public bool Delete(string clipId) => _clips.Remove(clipId);

        public bool Touch(string clipId, long updatedAt)
        {
            Touched.Add(clipId);
            return _clips.ContainsKey(clipId);
        }

        public void IgnoreCurrentClipboardState() { }
    }

    /// <summary>A top-level window with a multi-line edit control, used as the paste target.</summary>
    private sealed class Host : IDisposable
    {
        private IntPtr _window;
        private IntPtr _edit;

        public IntPtr Window => _window;

        public static Host Create()
        {
            Host host = new();

            WNDCLASSEX windowClass = new()
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = GetDefWindowProcPointer(),
                hInstance = GetModuleHandle(null),
                lpszClassName = HostClassName,
                hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW),
            };

            RegisterClassEx(ref windowClass);

            host._window = CreateWindowEx(
                0,
                HostClassName,
                "CopyPasta paste test",
                WS_OVERLAPPEDWINDOW,
                100,
                100,
                520,
                220,
                IntPtr.Zero,
                IntPtr.Zero,
                GetModuleHandle(null),
                IntPtr.Zero);

            if (host._window == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not create the test window.");
            }

            host._edit = CreateWindowEx(
                0,
                "EDIT",
                string.Empty,
                WS_CHILD | WS_VISIBLE | ES_MULTILINE | ES_AUTOVSCROLL,
                8,
                8,
                490,
                160,
                host._window,
                IdEdit,
                GetModuleHandle(null),
                IntPtr.Zero);

            if (host._edit == IntPtr.Zero)
            {
                throw new InvalidOperationException("Could not create the test edit control.");
            }

            ShowWindow(host._window, SW_SHOW);
            return host;
        }

        public bool TryBringToForeground()
        {
            NativeSetForeground(_window);
            SetFocus(_edit);
            PumpFor(TimeSpan.FromMilliseconds(300));

            // Focus the edit control again after pumping: activation can move it.
            SetFocus(_edit);
            return true;
        }

        public void PumpFor(TimeSpan duration)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            while (elapsed.Elapsed < duration)
            {
                while (PeekMessage(out MSG message, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }

                Thread.Sleep(5);
            }
        }

        public void ClearEdit() => SetWindowText(_edit, string.Empty);

        public string ReadEditText()
        {
            int length = (int)SendMessage(_edit, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
            if (length <= 0)
            {
                return string.Empty;
            }

            StringBuilder buffer = new(length + 1);
            SendMessage(_edit, WM_GETTEXT, (IntPtr)(length + 1), buffer);
            return buffer.ToString();
        }

        public void Dispose()
        {
            if (_window != IntPtr.Zero)
            {
                DestroyWindow(_window);
                _window = IntPtr.Zero;
            }
        }

        private static void NativeSetForeground(IntPtr window) => SetForegroundWindow(window);

        private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
        private const uint WS_CHILD = 0x40000000;
        private const uint WS_VISIBLE = 0x10000000;
        private const uint ES_MULTILINE = 0x0004;
        private const uint ES_AUTOVSCROLL = 0x0040;
        private const int SW_SHOW = 5;
        private const uint PM_REMOVE = 0x0001;
        private const uint WM_GETTEXT = 0x000D;
        private const uint WM_GETTEXTLENGTH = 0x000E;
        private const int IDC_ARROW = 32512;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            IntPtr hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(
            uint dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            IntPtr hWndParent,
            int hMenu,
            IntPtr hInstance,
            IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetWindowText(IntPtr hWnd, string lpString);

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(
            out MSG lpMsg,
            IntPtr hWnd,
            uint wMsgFilterMin,
            uint wMsgFilterMax,
            uint wRemoveMsg);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandleW(string? lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        /// <summary>
        /// The host window needs no behaviour of its own, so it uses DefWindowProc directly
        /// rather than a managed delegate that would have to be kept alive.
        /// </summary>
        private static IntPtr GetDefWindowProcPointer()
        {
            IntPtr user32 = GetModuleHandleW("user32.dll");
            return GetProcAddress(user32, "DefWindowProcW");
        }
    }
}
