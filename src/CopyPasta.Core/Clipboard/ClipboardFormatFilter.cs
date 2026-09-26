namespace CopyPasta.Core.Clipboard;

/// <summary>
/// Decides which of the clipboard's offered formats are worth storing.
/// Port of macOS <c>PasteboardAvailableType.availableTypes(from:...)</c>.
/// </summary>
/// <remarks>
/// This is the subtlest logic in the application: every rule below exists because some
/// real-world app does something awkward. The rule order is significant and matches the
/// macOS original.
/// </remarks>
public static class ClipboardFormatFilter
{
    /// <summary>
    /// Formats whose mere presence means "no clipboard manager should record this clip".
    /// Port of the macOS <c>org.nspasteboard.TransientType</c> check.
    /// </summary>
    private static readonly ClipboardFormat[] DoNotRecordMarkers =
    [
        ClipboardFormat.ExcludeFromMonitorProcessing,
        ClipboardFormat.ClipboardViewerIgnore,
    ];

    public static ClipboardSelection Select(ClipboardSnapshot snapshot, ClipboardFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);

        // Distinct, but order-preserving: the first format is the offering app's preferred
        // representation, and several rules below depend on that.
        List<ClipboardFormat> unique = Distinct(snapshot.Formats);

        // Rule 1: honour an explicit opt-out from clipboard monitoring.
        if (unique.Any(format => DoNotRecordMarkers.Contains(format)))
        {
            return ClipboardSelection.None;
        }

        // Rule 2: when sensitive clips are ignored, do not store them. A clip is sensitive
        // when the source app set CanIncludeInClipboardHistory to 0.
        bool isConcealed = snapshot.CanIncludeInClipboardHistory == false;
        if (options.IgnoresConcealedContent && isConcealed)
        {
            return ClipboardSelection.None;
        }

        // Rule 3: when cross-device clips are ignored, do not store clips from other devices.
        bool isFromCloud = snapshot.IsFromCloudClipboard;
        if (options.IgnoresCloudClipboard && isFromCloud)
        {
            return ClipboardSelection.None;
        }

        // Rule 4: a cloud clip's file paths can point at storage that does not exist on this
        // machine, or that is cleaned up once the transfer completes. If a file reference is
        // the primary representation, the clip is unusable later, so skip it entirely.
        // Otherwise fall through and keep the other representations (rule 5.1 drops the
        // file reference on its own).
        if (isFromCloud && unique.Count > 0 && IsFileReference(unique[0]))
        {
            return ClipboardSelection.None;
        }

        // Rule 5: per-format selection.
        List<ClipboardFormat> selected = [];
        foreach (ClipboardFormat format in unique)
        {
            // 5.1 Never store a cloud clip's file references (see rule 4).
            if (isFromCloud && IsFileReference(format))
            {
                continue;
            }

            // 5.2 The format must map to a known category the user has left enabled.
            ClipContentType? contentType = MapToContentType(format);
            if (contentType is null || !options.EnabledTypes.Contains(contentType.Value))
            {
                continue;
            }

            // 5.3 Skip a format that a richer one already covers (e.g. TIFF when PNG is
            // offered) — storing both wastes bytes and risks lossy round-trips.
            if (IsCoveredBy(format, unique))
            {
                continue;
            }

            // 5.4 Skip a legacy format when its modern equivalent is offered. Windows
            // auto-synthesises several of these (CF_TEXT from CF_UNICODETEXT), so without
            // this rule every text clip would be stored two or three times over.
            ClipboardFormat? modern = ModernEquivalent(format);
            if (modern is not null && unique.Contains(modern.Value))
            {
                continue;
            }

            selected.Add(format);
        }

        if (selected.Count == 0)
        {
            return ClipboardSelection.None;
        }

        return new ClipboardSelection
        {
            Formats = selected,
            IsConcealed = isConcealed,
            IsFromCloudClipboard = isFromCloud,
        };
    }

    /// <summary>Which settings category a format belongs to, or null if it is not storable.</summary>
    internal static ClipContentType? MapToContentType(ClipboardFormat format)
    {
        if (format == ClipboardFormat.UnicodeText ||
            format == ClipboardFormat.Text ||
            format == ClipboardFormat.OemText)
        {
            return ClipContentType.Text;
        }

        if (format == ClipboardFormat.Rtf)
        {
            return ClipContentType.Rtf;
        }

        if (format == ClipboardFormat.Html)
        {
            return ClipContentType.Html;
        }

        if (format == ClipboardFormat.Pdf)
        {
            return ClipContentType.Pdf;
        }

        if (format == ClipboardFormat.Hdrop ||
            format == ClipboardFormat.FileNameW ||
            format == ClipboardFormat.FileName)
        {
            return ClipContentType.Files;
        }

        if (format == ClipboardFormat.UrlW || format == ClipboardFormat.Url)
        {
            return ClipContentType.Url;
        }

        if (format == ClipboardFormat.Png ||
            format == ClipboardFormat.DibV5 ||
            format == ClipboardFormat.Dib ||
            format == ClipboardFormat.Tiff ||
            format == ClipboardFormat.Jfif ||
            format == ClipboardFormat.Gif)
        {
            return ClipContentType.Image;
        }

        // Not storable, by design:
        //  - CF_BITMAP is an HBITMAP handle rather than a byte blob. Windows synthesises
        //    CF_DIB from it, so dropping it here loses nothing.
        //  - CF_LOCALE is a codepage hint that only has meaning alongside CF_TEXT.
        //  - marker formats are flags, carried on ClipboardSelection instead.
        //  - anything else is an app-private format that no other app could consume.
        return null;
    }

    /// <summary>
    /// True when a richer offered format supersedes this one. Image preference order is
    /// PNG &gt; CF_DIBV5 &gt; CF_DIB &gt; TIFF: PNG is compact and lossless, DIBV5 carries an
    /// alpha channel that plain CF_DIB drops.
    /// </summary>
    /// <remarks>
    /// macOS only expresses "prefer PNG over TIFF" here; the DIB ordering is the Windows
    /// extension of the same idea.
    /// </remarks>
    internal static bool IsCoveredBy(ClipboardFormat format, IReadOnlyList<ClipboardFormat> offered)
    {
        if (format == ClipboardFormat.DibV5)
        {
            return offered.Contains(ClipboardFormat.Png);
        }

        if (format == ClipboardFormat.Dib)
        {
            return offered.Contains(ClipboardFormat.Png) || offered.Contains(ClipboardFormat.DibV5);
        }

        if (format == ClipboardFormat.Tiff ||
            format == ClipboardFormat.Jfif ||
            format == ClipboardFormat.Gif)
        {
            return offered.Contains(ClipboardFormat.Png) ||
                   offered.Contains(ClipboardFormat.DibV5) ||
                   offered.Contains(ClipboardFormat.Dib);
        }

        return false;
    }

    /// <summary>
    /// The modern replacement for a legacy format, if any. Port of the macOS
    /// <c>modernType</c> mapping.
    /// </summary>
    internal static ClipboardFormat? ModernEquivalent(ClipboardFormat format)
    {
        if (format == ClipboardFormat.Text || format == ClipboardFormat.OemText)
        {
            return ClipboardFormat.UnicodeText;
        }

        if (format == ClipboardFormat.FileName || format == ClipboardFormat.FileNameW)
        {
            return ClipboardFormat.Hdrop;
        }

        if (format == ClipboardFormat.Url)
        {
            return ClipboardFormat.UrlW;
        }

        return null;
    }

    internal static bool IsFileReference(ClipboardFormat format) =>
        format == ClipboardFormat.Hdrop ||
        format == ClipboardFormat.FileNameW ||
        format == ClipboardFormat.FileName;

    private static List<ClipboardFormat> Distinct(IReadOnlyList<ClipboardFormat> formats)
    {
        List<ClipboardFormat> unique = new(formats.Count);
        HashSet<ClipboardFormat> seen = [];
        foreach (ClipboardFormat format in formats)
        {
            if (!format.IsUnnamed && seen.Add(format))
            {
                unique.Add(format);
            }
        }

        return unique;
    }
}
