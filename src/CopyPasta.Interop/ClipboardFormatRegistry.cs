using System.Collections.Concurrent;
using System.Text;
using CopyPasta.Core.Clipboard;

namespace CopyPasta.Interop;

/// <summary>
/// Translates between Win32 numeric clipboard format ids and <see cref="ClipboardFormat"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered format ids are only stable within a boot session, so they are never persisted —
/// this is the single place that knows about them, and the rest of the app works in names.
/// </para>
/// <para>
/// Lookups are cached in both directions. <c>RegisterClipboardFormat</c> is idempotent and
/// returns the existing id for a name already registered by any process, so registering a name
/// we only intend to read is harmless.
/// </para>
/// </remarks>
public sealed class ClipboardFormatRegistry
{
    private static readonly (uint Id, ClipboardFormat Format)[] StandardFormats =
    [
        (1, ClipboardFormat.Text),
        (2, ClipboardFormat.Bitmap),
        (7, ClipboardFormat.OemText),
        (8, ClipboardFormat.Dib),
        (13, ClipboardFormat.UnicodeText),
        (15, ClipboardFormat.Hdrop),
        (16, ClipboardFormat.Locale),
        (17, ClipboardFormat.DibV5),
    ];

    private readonly ConcurrentDictionary<uint, ClipboardFormat> _byId = new();
    private readonly ConcurrentDictionary<ClipboardFormat, uint> _byFormat = new();

    public ClipboardFormatRegistry()
    {
        foreach ((uint id, ClipboardFormat format) in StandardFormats)
        {
            _byId[id] = format;
            _byFormat[format] = id;
        }
    }

    /// <summary>
    /// The format for a numeric id. Falls back to a synthetic <c>CF_#nnnnn</c> name for an
    /// id whose name cannot be retrieved, so an unnameable format is still distinguishable
    /// rather than colliding with others.
    /// </summary>
    public ClipboardFormat Resolve(uint id)
    {
        if (_byId.TryGetValue(id, out ClipboardFormat cached))
        {
            return cached;
        }

        StringBuilder buffer = new(512);
        int length = NativeMethods.GetClipboardFormatName(id, buffer, buffer.Capacity);
        ClipboardFormat format = length > 0
            ? ClipboardFormat.FromName(buffer.ToString(0, length))
            : ClipboardFormat.FromName($"CF_#{id}");

        _byId[id] = format;
        _byFormat.TryAdd(format, id);
        return format;
    }

    /// <summary>
    /// The numeric id for a format, registering the name if necessary. Returns 0 when the name
    /// cannot be registered.
    /// </summary>
    public uint ResolveId(ClipboardFormat format)
    {
        if (format.IsUnnamed)
        {
            return 0;
        }

        if (_byFormat.TryGetValue(format, out uint cached))
        {
            return cached;
        }

        uint id = NativeMethods.RegisterClipboardFormat(format.Name);
        if (id == 0)
        {
            return 0;
        }

        _byFormat[format] = id;
        _byId.TryAdd(id, format);
        return id;
    }
}
