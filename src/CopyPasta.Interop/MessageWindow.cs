using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CopyPasta.Interop;

/// <summary>
/// A message-only window: no presence on screen, in the taskbar or in Alt+Tab, and it receives
/// only messages.
/// </summary>
/// <remarks>
/// <para>
/// The Windows equivalent of a macOS <c>LSUIElement</c> app's invisible plumbing. Both the
/// clipboard monitor and the tray icon need an HWND to receive notifications on, and both need it
/// on a thread with a message loop.
/// </para>
/// <para>
/// Must be constructed on the thread that will pump messages, and used only from that thread:
/// Win32 delivers messages to the thread that created the window.
/// </para>
/// </remarks>
public sealed class MessageWindow : IDisposable
{
    private const int ErrorClassAlreadyExists = 1410;

    private readonly WndProc _wndProc;
    private readonly string _className;
    private IntPtr _handle;
    private bool _disposed;

    public MessageWindow(string className)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(className);

        _className = className;

        // Held in a field so the delegate is not collected while Win32 holds the pointer to it.
        _wndProc = WindowProcedure;

        WNDCLASSEX windowClass = new()
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = className,
        };

        if (RegisterClassEx(ref windowClass) == 0)
        {
            int error = Marshal.GetLastWin32Error();

            // Benign: a previous instance in this process registered the class, and the
            // registration outlives it.
            if (error != ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error, $"Could not register the window class {className}.");
            }
        }

        _handle = CreateWindowEx(
            0,
            className,
            className,
            0,
            0,
            0,
            0,
            0,
            NativeMethods.HWND_MESSAGE,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (_handle == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                $"Could not create the message window {className}.");
        }
    }

    /// <summary>
    /// Raised for every message. Set <see cref="MessageEventArgs.Handled"/> to stop the message
    /// reaching <c>DefWindowProc</c>.
    /// </summary>
    public event EventHandler<MessageEventArgs>? MessageReceived;

    /// <summary>Raised when a handler throws. The window keeps running.</summary>
    public event EventHandler<Exception>? HandlerFailed;

    public IntPtr Handle => _handle;

    private IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        MessageEventArgs args = new(message, wParam, lParam);

        // Never let an exception unwind through native code: that tears the process down with no
        // usable diagnostics.
        try
        {
            MessageReceived?.Invoke(this, args);
        }
        catch (Exception exception)
        {
            HandlerFailed?.Invoke(this, exception);
        }

        return args.Handled ? args.Result : DefWindowProc(hwnd, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_handle != IntPtr.Zero)
        {
            DestroyWindow(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}

/// <param name="Message">The Win32 message id.</param>
public sealed class MessageEventArgs(uint message, IntPtr wParam, IntPtr lParam) : EventArgs
{
    public uint Message { get; } = message;

    public IntPtr WParam { get; } = wParam;

    public IntPtr LParam { get; } = lParam;

    /// <summary>Set to true to stop the message reaching <c>DefWindowProc</c>.</summary>
    public bool Handled { get; set; }

    /// <summary>The value to return from the window procedure when handled.</summary>
    public IntPtr Result { get; set; }
}
