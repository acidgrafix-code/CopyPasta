using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CopyPasta.Interop;

/// <summary>
/// Raises an event whenever the clipboard changes.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the macOS 500 ms <c>changeCount</c> poll outright. <c>WM_CLIPBOARDUPDATE</c> is
/// delivered by the OS, so the app wakes only when something actually happens instead of 172,800
/// times a day.
/// </para>
/// <para>
/// Deliberately not the older <c>SetClipboardViewer</c> chain: a single misbehaving app in that
/// chain breaks notifications for everyone downstream of it.
/// </para>
/// <para>
/// Must be created and used on a thread with a message loop.
/// </para>
/// </remarks>
public sealed class ClipboardMonitor : IDisposable
{
    private readonly MessageWindow _window;
    private bool _listening;
    private bool _disposed;

    public ClipboardMonitor()
        : this(new MessageWindow("CopyPasta.ClipboardMonitor"), ownsWindow: true)
    {
    }

    /// <summary>Shares an existing message window rather than creating its own.</summary>
    public ClipboardMonitor(MessageWindow window)
        : this(window, ownsWindow: false)
    {
    }

    private ClipboardMonitor(MessageWindow window, bool ownsWindow)
    {
        ArgumentNullException.ThrowIfNull(window);

        _window = window;
        OwnsWindow = ownsWindow;
        _window.MessageReceived += OnMessage;
    }

    /// <summary>
    /// Raised on the monitor's thread when the clipboard changes. Handlers should return quickly:
    /// a slow handler blocks the message loop, and the clipboard is a shared resource other apps
    /// are waiting on.
    /// </summary>
    public event EventHandler? ClipboardChanged;

    /// <summary>Raised when a <see cref="ClipboardChanged"/> handler throws. Monitoring continues.</summary>
    public event EventHandler<Exception>? UnhandledMonitorException;

    public bool IsListening => _listening;

    /// <summary>The window handle, usable as a clipboard owner.</summary>
    public IntPtr WindowHandle => _window.Handle;

    private bool OwnsWindow { get; }

    /// <summary>Subscribes to clipboard notifications.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_listening)
        {
            return;
        }

        if (!NativeMethods.AddClipboardFormatListener(_window.Handle))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not subscribe to clipboard notifications.");
        }

        _listening = true;
    }

    /// <summary>Unsubscribes. Safe to call when not listening.</summary>
    public void Stop()
    {
        if (!_listening)
        {
            return;
        }

        NativeMethods.RemoveClipboardFormatListener(_window.Handle);
        _listening = false;
    }

    private void OnMessage(object? sender, MessageEventArgs args)
    {
        if (args.Message != NativeMethods.WM_CLIPBOARDUPDATE)
        {
            return;
        }

        args.Handled = true;

        try
        {
            ClipboardChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            UnhandledMonitorException?.Invoke(this, exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Stop();
        _window.MessageReceived -= OnMessage;

        if (OwnsWindow)
        {
            _window.Dispose();
        }
    }
}
