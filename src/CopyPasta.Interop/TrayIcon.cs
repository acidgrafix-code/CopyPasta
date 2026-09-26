using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CopyPasta.Interop;

/// <summary>Where the user clicked the tray icon.</summary>
public enum TrayClick
{
    Left,
    Right,
}

/// <summary>
/// A notification-area icon. The Windows counterpart of macOS <c>NSStatusItem</c>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <c>NSStatusItem</c>, a tray icon has no built-in menu: Windows delivers a click
/// notification and the app decides what to show. That is why the menu is popped explicitly rather
/// than attached.
/// </para>
/// <para>
/// The icon is drawn at runtime rather than shipped as an asset — see
/// <see cref="TrayIconGlyph"/> for why.
/// </para>
/// </remarks>
public sealed class TrayIcon : IDisposable
{
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint WM_TRAYCALLBACK = 0x0400 + 1;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;

    private readonly MessageWindow _window;
    private readonly uint _id;
    private IntPtr _icon;
    private bool _added;
    private bool _disposed;

    public TrayIcon(MessageWindow window, string toolTip, uint id = 1)
    {
        ArgumentNullException.ThrowIfNull(window);

        _window = window;
        _id = id;
        ToolTip = toolTip ?? string.Empty;
        _window.MessageReceived += OnMessage;
    }

    /// <summary>Raised when the user clicks the icon.</summary>
    public event EventHandler<TrayClick>? Clicked;

    public string ToolTip { get; private set; }

    public bool IsVisible { get; private set; }

    /// <summary>
    /// Shows the icon, or updates it if already shown.
    /// </summary>
    /// <param name="icon">
    /// An icon handle this instance takes ownership of and destroys. Pass one from
    /// <see cref="TrayIconGlyph"/>.
    /// </param>
    public void Show(IntPtr icon)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (icon == IntPtr.Zero)
        {
            throw new ArgumentException("An icon handle is required.", nameof(icon));
        }

        IntPtr previous = _icon;
        _icon = icon;

        NOTIFYICONDATA data = BuildData();

        if (!Shell_NotifyIcon(_added ? NIM_MODIFY : NIM_ADD, ref data))
        {
            // Restore the previous handle so ownership stays consistent on failure.
            _icon = previous;
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not add the tray icon.");
        }

        _added = true;
        IsVisible = true;

        if (previous != IntPtr.Zero && previous != _icon)
        {
            DestroyIcon(previous);
        }
    }

    /// <summary>
    /// Removes the icon from the notification area. Port of the macOS "hidden" status-item mode.
    /// </summary>
    public void Hide()
    {
        if (!_added)
        {
            return;
        }

        NOTIFYICONDATA data = new()
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _window.Handle,
            uID = _id,
        };

        Shell_NotifyIcon(NIM_DELETE, ref data);
        _added = false;
        IsVisible = false;
    }

    public void SetToolTip(string toolTip)
    {
        ToolTip = toolTip ?? string.Empty;

        if (!_added)
        {
            return;
        }

        NOTIFYICONDATA data = BuildData();
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private NOTIFYICONDATA BuildData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _window.Handle,
        uID = _id,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = WM_TRAYCALLBACK,
        hIcon = _icon,
        szTip = ToolTip.Length > 127 ? ToolTip[..127] : ToolTip,
    };

    private void OnMessage(object? sender, MessageEventArgs args)
    {
        if (args.Message != WM_TRAYCALLBACK)
        {
            return;
        }

        args.Handled = true;

        // The mouse message is in the low word of lParam.
        uint mouseMessage = (uint)(args.LParam.ToInt64() & 0xFFFF);

        TrayClick? click = mouseMessage switch
        {
            WM_LBUTTONUP => TrayClick.Left,
            WM_RBUTTONUP => TrayClick.Right,
            _ => null,
        };

        if (click is not null)
        {
            Clicked?.Invoke(this, click.Value);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Hide();
        _window.MessageReceived -= OnMessage;

        if (_icon != IntPtr.Zero)
        {
            DestroyIcon(_icon);
            _icon = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
