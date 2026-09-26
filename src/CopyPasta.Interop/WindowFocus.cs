using System.Diagnostics;
using CopyPasta.Core.Paste;

namespace CopyPasta.Interop;

/// <summary>Remembers and restores the focused window.</summary>
/// <remarks>
/// <para>
/// The most failure-prone step in the whole paste path, and one macOS does not have at all.
/// Showing our menu makes us the foreground process, so the target window must be captured
/// beforehand and explicitly restored before any keystroke is synthesised.
/// </para>
/// <para>
/// <c>SetForegroundWindow</c> is both restricted and asynchronous. It is permitted here because
/// we are the foreground process at the moment we call it (our own menu was just dismissed), but
/// it returns before the switch completes — so the restore is confirmed by polling rather than
/// assumed. Sending Ctrl+V a moment too early delivers it to whatever still had focus, which is
/// exactly the "paste went nowhere" bug.
/// </para>
/// </remarks>
public sealed class WindowFocus : IWindowFocus
{
    private readonly TimeSpan _restoreTimeout;
    private readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(5);

    public WindowFocus(TimeSpan? restoreTimeout = null) =>
        _restoreTimeout = restoreTimeout ?? TimeSpan.FromMilliseconds(400);

    public FocusToken Capture() => new((long)NativeMethods.GetForegroundWindow());

    public bool TryRestore(FocusToken token)
    {
        if (!token.IsValid)
        {
            return false;
        }

        IntPtr target = (IntPtr)token.Handle;

        // The window may have closed while the menu was open.
        if (!NativeMethods.IsWindow(target))
        {
            return false;
        }

        if (NativeMethods.GetForegroundWindow() == target)
        {
            return true;
        }

        NativeMethods.SetForegroundWindow(target);

        // Deliberately ignoring the return value: it reports false in cases where the switch
        // still happens, so the observed foreground window is the only trustworthy signal.
        Stopwatch elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < _restoreTimeout)
        {
            if (NativeMethods.GetForegroundWindow() == target)
            {
                return true;
            }

            Thread.Sleep(_pollInterval);
        }

        return NativeMethods.GetForegroundWindow() == target;
    }
}
