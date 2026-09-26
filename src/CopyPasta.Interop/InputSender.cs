using System.Runtime.InteropServices;
using CopyPasta.Core.Paste;

namespace CopyPasta.Interop;

/// <summary>Synthesises the Ctrl+V keystroke that triggers a paste.</summary>
/// <remarks>
/// <para>
/// The Windows counterpart of the macOS <c>CGEvent</c> path, and considerably simpler: no
/// Accessibility permission, no prompt, no System Settings deep link. The whole permission flow
/// the macOS app carries around disappears.
/// </para>
/// <para>
/// What replaces it is a subtler problem. The user may be <em>holding</em> a modifier when they
/// pick the menu item — that is how the plain-text and delete actions are triggered. Those keys
/// are still physically down when the paste is sent, so a naive Ctrl+V arrives at the target as
/// Ctrl+Shift+V or Ctrl+Alt+V, which many applications bind to something else entirely. macOS
/// has the same hazard and does not address it. Here, any modifier found down is released
/// synthetically first, and restored afterwards so the user's physical key state stays
/// consistent with what applications believe.
/// </para>
/// <para>
/// Known limitation: <c>SendInput</c> cannot reach a process running at a higher integrity level
/// than ours. Pasting into an elevated application from a non-elevated CopyPasta silently does
/// nothing — this is UIPI, not a bug, and it is the practical Windows analogue of the macOS
/// Accessibility requirement. The return value cannot detect it; <c>SendInput</c> reports success
/// because the input was accepted into the queue.
/// </para>
/// </remarks>
public sealed class InputSender : IInputSender
{
    private static readonly ushort[] ModifierKeysToClear =
    [
        NativeMethods.VK_SHIFT,
        NativeMethods.VK_CONTROL,
        NativeMethods.VK_MENU,
        NativeMethods.VK_LWIN,
        NativeMethods.VK_RWIN,
    ];

    public bool TrySendPasteShortcut()
    {
        ushort[] held = ModifierKeysToClear.Where(IsDown).ToArray();

        List<NativeMethods.INPUT> inputs = [];

        // Release whatever the user is holding, so the target sees a clean Ctrl+V.
        foreach (ushort key in held)
        {
            inputs.Add(KeyUp(key));
        }

        inputs.Add(KeyDown(NativeMethods.VK_CONTROL));
        inputs.Add(KeyDown(NativeMethods.VK_V));
        inputs.Add(KeyUp(NativeMethods.VK_V));
        inputs.Add(KeyUp(NativeMethods.VK_CONTROL));

        NativeMethods.INPUT[] batch = [.. inputs];
        uint sent = NativeMethods.SendInput(
            (uint)batch.Length,
            batch,
            Marshal.SizeOf<NativeMethods.INPUT>());

        if (held.Length > 0)
        {
            RestoreHeldModifiers(held);
        }

        return sent == batch.Length;
    }

    /// <summary>
    /// Presses the modifiers the user is still physically holding back down.
    /// </summary>
    /// <remarks>
    /// Without this, releasing the physical key afterwards sends a second key-up for a key the
    /// system already believes is up, and applications that track modifier state by counting
    /// transitions get stuck. Only keys still physically down are restored, so a key released
    /// mid-paste is left alone.
    /// </remarks>
    private static void RestoreHeldModifiers(ushort[] held)
    {
        NativeMethods.INPUT[] restore = held.Where(IsDown).Select(KeyDown).ToArray();
        if (restore.Length == 0)
        {
            return;
        }

        NativeMethods.SendInput(
            (uint)restore.Length,
            restore,
            Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static bool IsDown(ushort virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private static NativeMethods.INPUT KeyDown(ushort virtualKey) => Key(virtualKey, isUp: false);

    private static NativeMethods.INPUT KeyUp(ushort virtualKey) => Key(virtualKey, isUp: true);

    private static NativeMethods.INPUT Key(ushort virtualKey, bool isUp) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        union = new NativeMethods.INPUTUNION
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = virtualKey,
                dwFlags = isUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
            },
        },
    };
}

/// <summary>Reads the live modifier-key state.</summary>
public sealed class ModifierKeys : IModifierKeys
{
    public bool IsPressed(ModifierKey key) => key switch
    {
        ModifierKey.Shift => IsDown(NativeMethods.VK_SHIFT),
        ModifierKey.Control => IsDown(NativeMethods.VK_CONTROL),
        ModifierKey.Alt => IsDown(NativeMethods.VK_MENU),
        ModifierKey.Windows => IsDown(NativeMethods.VK_LWIN) || IsDown(NativeMethods.VK_RWIN),
        _ => false,
    };

    private static bool IsDown(ushort virtualKey) =>
        (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}
