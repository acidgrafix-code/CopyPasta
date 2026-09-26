using System.ComponentModel;
using System.Runtime.InteropServices;
using CopyPasta.Core.Hotkeys;

namespace CopyPasta.Interop;

/// <summary>
/// Registers system-wide hotkeys through <c>RegisterHotKey</c>.
/// </summary>
/// <remarks>
/// <para>
/// Chosen over a <c>WH_KEYBOARD_LL</c> hook deliberately. A low-level hook sees every keystroke on
/// the machine, has to be fast enough not to stall the entire input queue, and silently steals
/// combinations other applications own. <c>RegisterHotKey</c> asks the system for exclusive
/// ownership and <em>tells you when someone else already has it</em> — which is the whole point of
/// the Phase 4 conflict requirement.
/// </para>
/// <para>
/// Hotkeys belong to the thread that registers them, and <c>WM_HOTKEY</c> is delivered to the
/// window that owns them, so this must be created and used on the message-loop thread.
/// </para>
/// </remarks>
public sealed class Win32HotkeyRegistrar : IHotkeyRegistrar, IDisposable
{
    /// <summary>ERROR_HOTKEY_ALREADY_REGISTERED.</summary>
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    /// <summary>
    /// MOD_NOREPEAT. Without it, holding the combination fires continuously and pops a menu per
    /// key repeat.
    /// </summary>
    private const uint ModNoRepeat = 0x4000;

    private const uint WM_HOTKEY = 0x0312;

    private readonly MessageWindow _window;
    private readonly HashSet<int> _registered = [];
    private bool _disposed;

    public Win32HotkeyRegistrar(MessageWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _window = window;
        _window.MessageReceived += OnMessage;
    }

    public event EventHandler<int>? Pressed;

    public HotkeyStatus TryRegister(int id, KeyCombination combination, out string? detail)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        detail = null;

        if (!combination.IsValid)
        {
            return HotkeyStatus.Invalid;
        }

        if (RegisterHotKey(_window.Handle, id, (uint)combination.Modifiers | ModNoRepeat, combination.VirtualKey))
        {
            _registered.Add(id);
            return HotkeyStatus.Registered;
        }

        int error = Marshal.GetLastWin32Error();

        if (error == ErrorHotkeyAlreadyRegistered)
        {
            // Includes combinations the shell itself reserves, such as Win+V for the built-in
            // clipboard history.
            return HotkeyStatus.Conflict;
        }

        detail = new Win32Exception(error).Message;
        return HotkeyStatus.Failed;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id))
        {
            UnregisterHotKey(_window.Handle, id);
        }
    }

    private void OnMessage(object? sender, MessageEventArgs args)
    {
        if (args.Message != WM_HOTKEY)
        {
            return;
        }

        args.Handled = true;
        Pressed?.Invoke(this, args.WParam.ToInt32());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (int id in _registered)
        {
            UnregisterHotKey(_window.Handle, id);
        }

        _registered.Clear();
        _window.MessageReceived -= OnMessage;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
