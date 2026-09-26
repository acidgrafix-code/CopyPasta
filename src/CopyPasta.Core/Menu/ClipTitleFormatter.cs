namespace CopyPasta.Core.Menu;

using CopyPasta.Core.Clipboard;

/// <summary>
/// Builds the text shown on menu items. Ports macOS <c>String.trimmedMenuTitle</c>,
/// <c>PasteboardHistory.typedTitle</c> and <c>MonospacedDigitFormatter</c>.
/// </summary>
public static class ClipTitleFormatter
{
    private const string Ellipsis = "...";

    /// <summary>
    /// Collapses a clip's text to one short line: whitespace trimmed, first line only, truncated
    /// with an ellipsis.
    /// </summary>
    /// <remarks>
    /// The first-line-only rule matters more than it looks: a copied code block or log excerpt
    /// would otherwise turn a single menu item into something unusable.
    /// </remarks>
    public static string TrimForMenu(string title, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(title);

        string trimmed = title.Trim();

        int lineEnd = trimmed.AsSpan().IndexOfAny('\r', '\n');
        if (lineEnd >= 0)
        {
            trimmed = trimmed[..lineEnd];
        }

        // Never shorter than the ellipsis itself, matching macOS.
        int limit = Math.Max(maximumLength, Ellipsis.Length);
        if (trimmed.Length <= limit)
        {
            return trimmed;
        }

        return string.Concat(trimmed.AsSpan(0, limit - Ellipsis.Length), Ellipsis);
    }

    /// <summary>
    /// A clip's title with a kind prefix for entries whose text alone would not identify them.
    /// Port of <c>typedTitle</c>.
    /// </summary>
    public static string TypedTitle(string title, ClipboardFormat? primaryFormat, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(title);

        string? prefix = KindPrefix(primaryFormat);
        string trimmed = TrimForMenu(title, maximumLength);

        if (prefix is null)
        {
            return trimmed;
        }

        return trimmed.Length == 0 ? prefix : $"{prefix} {trimmed}";
    }

    /// <summary>Prefixes the title with its list number. Port of <c>numberedTitle</c>.</summary>
    /// <remarks>
    /// macOS renders the digits with a monospaced numeral variant so the titles line up. Win32
    /// menus take plain strings and use the system menu font, so that refinement has no
    /// equivalent here; the text itself is identical.
    /// </remarks>
    public static string Numbered(string title, int listNumber, bool showsNumber)
    {
        ArgumentNullException.ThrowIfNull(title);
        return showsNumber ? $"{listNumber}. {title}" : title;
    }

    /// <summary>A folder's range title, such as "11 - 20". Port of <c>rangeTitle</c>.</summary>
    public static string Range(int firstNumber, int lastNumber) => $"{firstNumber} - {lastNumber}";

    /// <summary>
    /// The longer preview shown on hover, or null when tooltips are off. Port of
    /// <c>PasteboardHistory.toolTip</c>.
    /// </summary>
    /// <remarks>
    /// Built from the untrimmed text on purpose: the tooltip exists precisely to show what the
    /// one-line title had to cut.
    /// </remarks>
    public static string? ToolTip(string title, bool showsToolTips, int maximumLength)
    {
        ArgumentNullException.ThrowIfNull(title);

        if (!showsToolTips || maximumLength <= 0 || title.Length == 0)
        {
            return null;
        }

        return title.Length <= maximumLength ? title : title[..maximumLength];
    }

    private static string? KindPrefix(ClipboardFormat? primaryFormat)
    {
        if (primaryFormat is not { } format)
        {
            return null;
        }

        if (format == ClipboardFormat.Png ||
            format == ClipboardFormat.DibV5 ||
            format == ClipboardFormat.Dib ||
            format == ClipboardFormat.Tiff ||
            format == ClipboardFormat.Jfif ||
            format == ClipboardFormat.Gif)
        {
            return "(Image)";
        }

        if (format == ClipboardFormat.Pdf)
        {
            return "(PDF)";
        }

        if (format == ClipboardFormat.Hdrop ||
            format == ClipboardFormat.FileNameW ||
            format == ClipboardFormat.FileName)
        {
            return "(Files)";
        }

        if (format == ClipboardFormat.Html)
        {
            return "(HTML)";
        }

        return null;
    }
}
