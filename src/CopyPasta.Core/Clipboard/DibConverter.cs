using System.Buffers.Binary;

namespace CopyPasta.Core.Clipboard;

/// <summary>
/// Turns a clipboard DIB into a decodable BMP.
/// </summary>
/// <remarks>
/// <para>
/// <c>CF_DIB</c> and <c>CF_DIBV5</c> hold a bare <c>BITMAPINFOHEADER</c> (or
/// <c>BITMAPV5HEADER</c>) followed by the palette and pixels — everything a <c>.bmp</c> file has
/// <em>except</em> its leading 14-byte <c>BITMAPFILEHEADER</c>. GDI+ decodes files, so handing it a
/// clipboard DIB directly fails outright, which is why copied images produced no menu thumbnail
/// until this existed.
/// </para>
/// <para>
/// Prepending the missing header is the whole conversion; the pixel data is untouched.
/// </para>
/// </remarks>
public static class DibConverter
{
    private const int FileHeaderSize = 14;
    private const int MinimumInfoHeaderSize = 40;
    private const uint BI_BITFIELDS = 3;

    /// <summary>
    /// Wraps DIB bytes in a BMP file header. Returns false for anything that does not look like a
    /// DIB, since the bytes come from arbitrary applications.
    /// </summary>
    public static bool TryWrapAsBitmapFile(ReadOnlySpan<byte> dib, out byte[] bitmapFile)
    {
        bitmapFile = [];

        if (dib.Length < MinimumInfoHeaderSize)
        {
            return false;
        }

        uint infoHeaderSize = BinaryPrimitives.ReadUInt32LittleEndian(dib);

        // 40 = BITMAPINFOHEADER, 108 = BITMAPV4HEADER, 124 = BITMAPV5HEADER. Anything else is not
        // a header this understands.
        if (infoHeaderSize is not (40 or 108 or 124) || dib.Length < infoHeaderSize)
        {
            return false;
        }

        ushort bitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        uint compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
        uint colorsUsed = BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);

        long paletteBytes = PaletteBytes(bitCount, colorsUsed);
        if (paletteBytes < 0)
        {
            return false;
        }

        // BITMAPINFOHEADER keeps its colour masks after the header rather than inside it; the V4 and
        // V5 headers have dedicated fields, so the masks must not be counted twice.
        long maskBytes = infoHeaderSize == MinimumInfoHeaderSize && compression == BI_BITFIELDS
            ? 12
            : 0;

        long pixelOffset = FileHeaderSize + infoHeaderSize + paletteBytes + maskBytes;
        if (pixelOffset > FileHeaderSize + dib.Length)
        {
            return false;
        }

        long fileSize = FileHeaderSize + (long)dib.Length;
        if (fileSize > int.MaxValue)
        {
            return false;
        }

        byte[] file = new byte[fileSize];

        file[0] = (byte)'B';
        file[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(2), (uint)fileSize);
        // Bytes 6..9 are the two reserved words, left zero.
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(10), (uint)pixelOffset);

        dib.CopyTo(file.AsSpan(FileHeaderSize));

        bitmapFile = file;
        return true;
    }

    /// <summary>
    /// Bytes the colour table occupies, or -1 if the bit count is not a valid DIB depth.
    /// </summary>
    private static long PaletteBytes(ushort bitCount, uint colorsUsed)
    {
        switch (bitCount)
        {
            case 1 or 4 or 8:
                // A zero count means the full table for that depth.
                uint entries = colorsUsed != 0 ? colorsUsed : 1u << bitCount;
                return (long)entries * 4;

            case 16 or 24 or 32:
                // These may still carry an optimisation palette, which biClrUsed declares.
                return (long)colorsUsed * 4;

            default:
                return -1;
        }
    }
}
