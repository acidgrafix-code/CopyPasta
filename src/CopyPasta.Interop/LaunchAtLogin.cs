using Microsoft.Win32;

namespace CopyPasta.Interop;

/// <summary>
/// Starts the app with Windows. Port of the macOS <c>SMAppService.mainApp</c> registration.
/// </summary>
/// <remarks>
/// <para>
/// Uses the per-user <c>Run</c> key rather than a scheduled task or a shortcut in the Startup
/// folder. It needs no elevation, it is visible to the user in Task Manager's Startup tab where
/// they would expect to find it, and disabling it there is respected — none of which is true of a
/// scheduled task.
/// </para>
/// <para>
/// Every operation is best-effort. Group policy or a security product can lock the key, and a
/// clipboard manager that refuses to start because it could not write a registry value would be a
/// worse outcome than one that simply does not auto-start.
/// </para>
/// </remarks>
public static class LaunchAtLogin
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>True when an entry for this value name exists.</summary>
    public static bool IsEnabled(string valueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(valueName) is string existing && existing.Length > 0;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return false;
        }
    }

    /// <summary>
    /// Adds or removes the entry. Returns false when the registry could not be written.
    /// </summary>
    /// <param name="executablePath">
    /// The executable to launch. Quoted when written, so a path containing spaces — which
    /// <c>C:\Program Files\…</c> always does — is not parsed as several arguments.
    /// </param>
    public static bool SetEnabled(string valueName, string executablePath, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (enabled)
            {
                key.SetValue(valueName, $"\"{executablePath}\"", RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return false;
        }
    }

    /// <summary>
    /// Brings the registry into line with the setting, and reports whether it now matches.
    /// </summary>
    /// <remarks>
    /// Called on every start so that a stale entry — left by a move or an uninstall that did not
    /// clean up — is rewritten to the current path rather than silently launching the wrong copy.
    /// </remarks>
    public static bool Apply(string valueName, string executablePath, bool enabled)
    {
        if (!enabled)
        {
            return !IsEnabled(valueName) || SetEnabled(valueName, executablePath, enabled: false);
        }

        return SetEnabled(valueName, executablePath, enabled: true);
    }

    private static bool IsExpected(Exception exception) =>
        exception is UnauthorizedAccessException
            or System.Security.SecurityException
            or IOException;
}
