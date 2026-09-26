using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CopyPasta.Core.Clipboard;

/// <summary>
/// Computes a clip's content hash. Port of the hashing in macOS
/// <c>PasteboardContent.init?(assets:)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The hash <em>is</em> the database primary key, which is what makes duplicate detection,
/// "overwrite same history", and asset reuse on re-copy fall out for free rather than needing
/// their own bookkeeping. That makes the algorithm load-bearing: changing it orphans every
/// stored row, so <c>ClipContentHasherTests</c> pins it to a golden vector.
/// </para>
/// <para>
/// Each asset contributes four fields, in order:
/// <c>[UInt64BE length of format name][format name UTF-8][UInt64BE length of data][data]</c>.
/// The length prefixes are the point: without them
/// <c>("A", "BC")</c> and <c>("AB", "C")</c> would serialise identically and collide.
/// </para>
/// <para>
/// This matches the macOS byte layout exactly, so the two ports share an algorithm — but not
/// hash <em>values</em>, since the format names themselves differ per platform
/// (<c>CF_UNICODETEXT</c> vs <c>public.utf8-plain-text</c>).
/// </para>
/// </remarks>
public static class ClipContentHasher
{
    public static string Compute(IReadOnlyList<ClipAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> lengthPrefix = stackalloc byte[sizeof(ulong)];

        foreach (ClipAsset asset in assets)
        {
            byte[] formatName = Encoding.UTF8.GetBytes(asset.Format.Name);
            AppendLengthPrefixed(hash, formatName, lengthPrefix);
            AppendLengthPrefixed(hash, asset.Data, lengthPrefix);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void AppendLengthPrefixed(IncrementalHash hash, byte[] value, Span<byte> lengthPrefix)
    {
        BinaryPrimitives.WriteUInt64BigEndian(lengthPrefix, (ulong)value.Length);
        hash.AppendData(lengthPrefix);
        hash.AppendData(value);
    }
}
