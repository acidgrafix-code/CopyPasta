using System.Buffers.Binary;
using System.Text;

namespace CopyPasta.Core.Tests;

/// <summary>
/// Assertion helpers. These exist mainly so tests can pass a <c>params</c> list of formats
/// instead of a collection expression, which keeps generic inference on
/// <c>Assert.Equal&lt;T&gt;</c> unambiguous.
/// </summary>
public static class TestAssert
{
    public static void AssertFormats(
        IReadOnlyList<ClipboardFormat> actual,
        params ClipboardFormat[] expected) =>
        Assert.Equal(expected, actual.ToArray());

    public static void AssertBytes(byte[] expected, byte[]? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected, actual);
    }
}

/// <summary>
/// Builds CF_HDROP payloads, so the DROPFILES parsing can be tested without a clipboard.
/// </summary>
public static class Dropfiles
{
    private const int HeaderSize = 20;

    /// <summary>A well-formed wide (UTF-16LE) DROPFILES blob.</summary>
    public static byte[] Wide(params string[] paths) => Build(paths, isWide: true, HeaderSize);

    /// <summary>A well-formed ANSI DROPFILES blob.</summary>
    public static byte[] Ansi(params string[] paths) => Build(paths, isWide: false, HeaderSize);

    /// <summary>
    /// A DROPFILES blob with an arbitrary <c>pFiles</c> offset, for malformed-input tests.
    /// </summary>
    public static byte[] WithListOffset(uint listOffset, params string[] paths) =>
        Build(paths, isWide: true, listOffset);

    private static byte[] Build(string[] paths, bool isWide, uint listOffset)
    {
        Encoding encoding = isWide ? Encoding.Unicode : Encoding.Latin1;
        string list = string.Concat(paths.Select(path => path + '\0')) + '\0';
        byte[] listBytes = encoding.GetBytes(list);

        int headerLength = (int)Math.Max(HeaderSize, listOffset);
        byte[] blob = new byte[headerLength + listBytes.Length];

        BinaryPrimitives.WriteUInt32LittleEndian(blob.AsSpan(0), listOffset);
        BinaryPrimitives.WriteInt32LittleEndian(blob.AsSpan(16), isWide ? 1 : 0);
        listBytes.CopyTo(blob, headerLength);

        return blob;
    }
}
