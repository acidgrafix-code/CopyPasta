using System.Buffers.Binary;

namespace CopyPasta.Core.Tests;

/// <summary>
/// The DIB-to-BMP wrapping that makes copied images decodable.
/// </summary>
/// <remarks>
/// Regression coverage for a real bug: without the prepended file header, GDI+ could not decode a
/// clipboard DIB at all, so every copied image silently ended up with no menu thumbnail while
/// colour swatches worked fine.
/// </remarks>
public class DibConverterTests
{
    /// <summary>Builds a BITMAPINFOHEADER-style DIB.</summary>
    private static byte[] Dib(
        uint headerSize = 40,
        ushort bitCount = 24,
        uint compression = 0,
        uint colorsUsed = 0,
        int pixelBytes = 64)
    {
        byte[] dib = new byte[headerSize + pixelBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(0), headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 4);   // width
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 4);   // height
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1); // planes
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), bitCount);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(16), compression);
        BinaryPrimitives.WriteUInt32LittleEndian(dib.AsSpan(32), colorsUsed);
        return dib;
    }

    private static uint PixelOffset(byte[] bitmapFile) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bitmapFile.AsSpan(10));

    // ---- The header ------------------------------------------------------------------

    [Fact]
    public void A_wrapped_dib_starts_with_the_BM_signature()
    {
        Assert.True(DibConverter.TryWrapAsBitmapFile(Dib(), out byte[] file));

        Assert.Equal((byte)'B', file[0]);
        Assert.Equal((byte)'M', file[1]);
    }

    [Fact]
    public void The_file_grows_by_exactly_the_header_size()
    {
        byte[] dib = Dib();

        Assert.True(DibConverter.TryWrapAsBitmapFile(dib, out byte[] file));

        Assert.Equal(dib.Length + 14, file.Length);
        Assert.Equal((uint)file.Length, BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(2)));
    }

    [Fact]
    public void The_pixel_data_is_copied_through_untouched()
    {
        byte[] dib = Dib();
        for (int index = 40; index < dib.Length; index++)
        {
            dib[index] = (byte)(index % 251);
        }

        Assert.True(DibConverter.TryWrapAsBitmapFile(dib, out byte[] file));

        Assert.Equal(dib, file.AsSpan(14).ToArray());
    }

    // ---- Pixel offset ------------------------------------------------------------------

    [Fact]
    public void A_true_colour_dib_has_pixels_right_after_the_header()
    {
        Assert.True(DibConverter.TryWrapAsBitmapFile(Dib(bitCount: 24), out byte[] file));

        Assert.Equal(14u + 40u, PixelOffset(file));
    }

    [Fact]
    public void An_eight_bit_dib_leaves_room_for_its_full_palette()
    {
        // 256 entries of 4 bytes.
        Assert.True(DibConverter.TryWrapAsBitmapFile(
            Dib(bitCount: 8, pixelBytes: 1024 + 64),
            out byte[] file));

        Assert.Equal(14u + 40u + 1024u, PixelOffset(file));
    }

    [Fact]
    public void A_declared_palette_size_is_honoured_over_the_full_table()
    {
        Assert.True(DibConverter.TryWrapAsBitmapFile(
            Dib(bitCount: 8, colorsUsed: 16, pixelBytes: 256),
            out byte[] file));

        Assert.Equal(14u + 40u + (16u * 4u), PixelOffset(file));
    }

    [Fact]
    public void A_bitfields_dib_leaves_room_for_the_three_colour_masks()
    {
        // BITMAPINFOHEADER keeps the masks after the header rather than inside it.
        Assert.True(DibConverter.TryWrapAsBitmapFile(
            Dib(bitCount: 32, compression: 3, pixelBytes: 128),
            out byte[] file));

        Assert.Equal(14u + 40u + 12u, PixelOffset(file));
    }

    [Fact]
    public void A_V5_bitfields_dib_does_not_double_count_the_masks()
    {
        // BITMAPV5HEADER has dedicated mask fields, so there are no trailing masks to skip.
        Assert.True(DibConverter.TryWrapAsBitmapFile(
            Dib(headerSize: 124, bitCount: 32, compression: 3, pixelBytes: 128),
            out byte[] file));

        Assert.Equal(14u + 124u, PixelOffset(file));
    }

    [Fact]
    public void A_V4_header_is_accepted()
    {
        Assert.True(DibConverter.TryWrapAsBitmapFile(
            Dib(headerSize: 108, bitCount: 32, pixelBytes: 128),
            out byte[] file));

        Assert.Equal(14u + 108u, PixelOffset(file));
    }

    // ---- Malformed input: must degrade, never throw ------------------------------------

    [Fact]
    public void Empty_input_is_rejected()
    {
        Assert.False(DibConverter.TryWrapAsBitmapFile([], out _));
    }

    [Fact]
    public void Input_shorter_than_a_header_is_rejected()
    {
        Assert.False(DibConverter.TryWrapAsBitmapFile(new byte[20], out _));
    }

    [Fact]
    public void An_unrecognised_header_size_is_rejected()
    {
        // A BITMAPCOREHEADER (12) and anything invented are both refused rather than guessed at.
        Assert.False(DibConverter.TryWrapAsBitmapFile(Dib(headerSize: 12), out _));
        Assert.False(DibConverter.TryWrapAsBitmapFile(Dib(headerSize: 99), out _));
    }

    [Fact]
    public void A_header_that_claims_more_than_the_buffer_holds_is_rejected()
    {
        byte[] truncated = new byte[60];
        BinaryPrimitives.WriteUInt32LittleEndian(truncated.AsSpan(0), 124);

        Assert.False(DibConverter.TryWrapAsBitmapFile(truncated, out _));
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData((ushort)7)]
    [InlineData((ushort)64)]
    public void An_invalid_bit_depth_is_rejected(ushort bitCount)
    {
        Assert.False(DibConverter.TryWrapAsBitmapFile(Dib(bitCount: bitCount), out _));
    }

    [Fact]
    public void A_palette_larger_than_the_buffer_is_rejected()
    {
        // 256 palette entries declared, but nowhere near enough bytes to hold them.
        Assert.False(DibConverter.TryWrapAsBitmapFile(
            Dib(bitCount: 8, pixelBytes: 8),
            out _));
    }
}
