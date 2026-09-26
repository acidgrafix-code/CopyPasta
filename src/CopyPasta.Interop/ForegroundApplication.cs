using System.Diagnostics;
using CopyPasta.Core.Capture;

namespace CopyPasta.Interop;

/// <summary>
/// Identifies the focused application. Port of the macOS <c>frontmostApplication</c> lookup.
/// </summary>
public sealed class ForegroundApplication : IForegroundApplication
{
    public ForegroundApplicationInfo? GetCurrent()
    {
        IntPtr window = NativeMethods.GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return null;
        }

        if (NativeMethods.GetWindowThreadProcessId(window, out uint processId) == 0 || processId == 0)
        {
            return null;
        }

        try
        {
            using Process process = Process.GetProcessById((int)processId);

            // MainModule throws for processes we cannot open — protected processes, and
            // anything running at higher integrity than us. The process name is still usable,
            // so a null path must not cost us the whole lookup.
            string? path = null;
            try
            {
                path = process.MainModule?.FileName;
            }
            catch (Exception exception) when (exception is InvalidOperationException
                                                  or System.ComponentModel.Win32Exception
                                                  or NotSupportedException)
            {
                path = null;
            }

            return new ForegroundApplicationInfo(process.ProcessName, path);
        }
        catch (Exception exception) when (exception is ArgumentException
                                              or InvalidOperationException
                                              or System.ComponentModel.Win32Exception)
        {
            // The process exited between the window query and the lookup.
            return null;
        }
    }
}
