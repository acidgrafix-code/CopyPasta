using System.Runtime.InteropServices;

namespace CopyPasta.Interop;

/// <summary>
/// Tells Explorer that something it caches has changed.
/// </summary>
/// <remarks>
/// <para>
/// Explorer caches shortcut icons by path in a database under
/// <c>%LOCALAPPDATA%\Microsoft\Windows\Explorer</c>, and it does not reliably notice when a
/// newly-installed target supplies one. The symptom is a freshly created Start Menu or desktop
/// shortcut drawn with the generic executable icon even though the binary carries the right one —
/// the shortcut, the icon and the shell API are all correct, only the cached bitmap is stale.
/// </para>
/// <para>
/// <c>SHChangeNotify</c> with <c>SHCNE_ASSOCCHANGED</c> is the documented way to ask the shell to
/// re-read what it has cached. It is cheap and safe to call, and is done once after install rather
/// than on every start.
/// </para>
/// </remarks>
public static class ShellNotification
{
    /// <summary>Icon or association data changed somewhere; re-read caches.</summary>
    private const int SHCNE_ASSOCCHANGED = 0x08000000;

    /// <summary>The arguments are unused for this event.</summary>
    private const uint SHCNF_IDLIST = 0x0000;

    /// <summary>
    /// Asks Explorer to refresh cached icons and associations.
    /// </summary>
    /// <remarks>
    /// Best-effort and never throws: a shell that ignores the hint costs a stale icon, which is not
    /// worth failing an install over.
    /// </remarks>
    public static void NotifyAssociationsChanged()
    {
        try
        {
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // Nothing to do; the icon simply refreshes on its own schedule.
        }
    }

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
