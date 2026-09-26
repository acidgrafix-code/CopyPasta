namespace CopyPasta.Core.Paste;

using CopyPasta.Core.Clipboard;

/// <summary>A modifier the user can hold while picking a history item.</summary>
/// <remarks>
/// macOS offers Command / Shift / Control / Option. The Windows set is the direct analogue,
/// except that Command becomes Ctrl.
/// </remarks>
public enum ModifierKey
{
    Shift,
    Control,
    Alt,
    Windows,
}

/// <summary>Reads the live state of the modifier keys.</summary>
/// <remarks>
/// Read at the moment the user activates a menu item, which is why this is a live query rather
/// than event data: Win32 menus do not report the modifier state with the click. Port of the
/// macOS <c>NSEvent.modifierFlags</c> reads in <c>PasteService</c>.
/// </remarks>
public interface IModifierKeys
{
    bool IsPressed(ModifierKey key);
}

/// <summary>Writes content to the system clipboard.</summary>
public interface IClipboardWriter
{
    /// <summary>
    /// Replaces the clipboard's contents. Returns false when the clipboard could not be opened.
    /// </summary>
    bool TryWrite(ClipboardWriteRequest request);
}

/// <param name="Assets">
/// The blobs to place, in priority order. Written verbatim — CF_HTML carries byte offsets in
/// its own header, so anything short of an exact copy corrupts it.
/// </param>
/// <param name="MarkAsConcealed">
/// Re-apply the sensitive-content marker, so replaying a password does not leak it into
/// Windows' own clipboard history.
/// </param>
public sealed record ClipboardWriteRequest(
    IReadOnlyList<ClipAsset> Assets,
    bool MarkAsConcealed = false)
{
    /// <summary>A plain-text-only write, for the "paste as plain text" modifier.</summary>
    public static ClipboardWriteRequest PlainText(string text, bool markAsConcealed = false)
    {
        ArgumentNullException.ThrowIfNull(text);

        byte[] data = System.Text.Encoding.Unicode.GetBytes(text + '\0');
        return new ClipboardWriteRequest(
            [new ClipAsset(ClipboardFormat.UnicodeText, data)],
            markAsConcealed);
    }
}

/// <summary>Synthesises the keystrokes that trigger a paste in the focused application.</summary>
public interface IInputSender
{
    /// <summary>
    /// Sends the paste shortcut. Returns false when the input could not be delivered.
    /// </summary>
    bool TrySendPasteShortcut();
}

/// <summary>An opaque reference to the window that had focus.</summary>
/// <param name="Handle">Platform window handle; 0 is never valid.</param>
public readonly record struct FocusToken(long Handle)
{
    public bool IsValid => Handle != 0;
}

/// <summary>
/// Remembers and restores which window had focus.
/// </summary>
/// <remarks>
/// This has no macOS counterpart, and it is the single biggest behavioural difference in the
/// paste path. On macOS a menu closing returns focus to the previous application by itself. On
/// Windows, showing our menu makes <em>us</em> the foreground process, so the target window has
/// to be captured before the menu opens and explicitly restored before any input is sent —
/// otherwise the paste lands in our own process and vanishes.
/// </remarks>
public interface IWindowFocus
{
    /// <summary>The currently focused window, to be restored later.</summary>
    FocusToken Capture();

    /// <summary>
    /// Brings the window back to the foreground, waiting for the change to take effect.
    /// </summary>
    /// <remarks>
    /// Focus changes are asynchronous on Windows: the call returns before the switch completes,
    /// and sending input too early delivers it to whatever still has focus. Implementations must
    /// confirm the switch rather than assume it.
    /// </remarks>
    bool TryRestore(FocusToken token);
}

/// <summary>
/// Lets the paste path tell the capture path to ignore the clipboard write it is about to make.
/// </summary>
/// <remarks>
/// Implemented by <c>ClipCaptureService</c>. Declared here as a narrow interface so the paste
/// path does not depend on the whole capture service.
/// </remarks>
public interface ICaptureSuppressor
{
    void IgnoreCurrentClipboardState();
}
