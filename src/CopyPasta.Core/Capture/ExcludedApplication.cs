namespace CopyPasta.Core.Capture;

using CopyPasta.Core.Clipboard;

/// <summary>An application the user does not want captured from.</summary>
/// <param name="Identifier">
/// Matched against the foreground process name, its executable path, and — see
/// <see cref="ExcludedApplicationMatcher"/> — against clipboard format names.
/// </param>
public sealed record ExcludedApplication(string Identifier);

/// <summary>
/// Decides whether a clip came from an excluded application.
/// Port of the macOS <c>ClipService.isExcludedApplication</c> check.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately two-pronged, exactly as macOS is. The obvious check — "is the focused app
/// excluded?" — misses the case the original comment calls out: extensions, menu-bar apps and
/// background helpers write to the clipboard <em>without ever taking focus</em>, so the
/// foreground process is someone else entirely.
/// </para>
/// <para>
/// The macOS fallback is that such apps leave a marker pasteboard type whose name the app's
/// bundle identifier starts with. The Windows analogue is the same idea against clipboard
/// format names: apps that register a private format typically name it after themselves
/// (<c>Acme.Suite.Internal</c>), so an exclusion of <c>Acme.Suite</c> should match it.
/// </para>
/// </remarks>
public sealed class ExcludedApplicationMatcher
{
    private readonly IForegroundApplication _foregroundApplication;

    public ExcludedApplicationMatcher(IForegroundApplication foregroundApplication)
    {
        ArgumentNullException.ThrowIfNull(foregroundApplication);
        _foregroundApplication = foregroundApplication;
    }

    public bool IsExcluded(
        IReadOnlyList<ExcludedApplication> excluded,
        IReadOnlyList<ClipboardFormat> offeredFormats)
    {
        ArgumentNullException.ThrowIfNull(excluded);
        ArgumentNullException.ThrowIfNull(offeredFormats);

        if (excluded.Count == 0)
        {
            return false;
        }

        ForegroundApplicationInfo? foreground = _foregroundApplication.GetCurrent();

        foreach (ExcludedApplication application in excluded)
        {
            if (string.IsNullOrWhiteSpace(application.Identifier))
            {
                continue;
            }

            if (MatchesForeground(application.Identifier, foreground) ||
                MatchesFormatMarker(application.Identifier, offeredFormats))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesForeground(string identifier, ForegroundApplicationInfo? foreground)
    {
        if (foreground is null)
        {
            return false;
        }

        // Accept either "notepad" or a full path, so the settings UI can store whichever it
        // has. Comparing the file name lets a path-based exclusion still match when the app
        // has been reinstalled somewhere else.
        if (string.Equals(identifier, foreground.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (foreground.ExecutablePath is null)
        {
            return false;
        }

        return string.Equals(identifier, foreground.ExecutablePath, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   Path.GetFileNameWithoutExtension(identifier),
                   foreground.ProcessName,
                   StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Minimum marker length. Guards against an exclusion for "PNGGleam" being matched by the
    /// standard "PNG" format — a hazard macOS does not have, because its type names are long
    /// reverse-DNS strings.
    /// </summary>
    private const int MinimumMarkerLength = 6;

    private static bool MatchesFormatMarker(
        string identifier,
        IReadOnlyList<ClipboardFormat> offeredFormats)
    {
        foreach (ClipboardFormat format in offeredFormats)
        {
            if (format.IsUnnamed || format.Name.Length < MinimumMarkerLength)
            {
                continue;
            }

            // Only app-private formats can act as an ownership marker. A standard or
            // well-known format says nothing about who put the clip there.
            if (ClipboardFormat.Known.Contains(format))
            {
                continue;
            }

            if (identifier.StartsWith(format.Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
