using System;
using Mixel.Core;
using Xunit;

public class PngLoaderTests
{
    [Fact]
    public void Load_RoundTripsPixels()
    {
        var src = TestImages.FromAscii(
            new[] { "#.", ".#" }, new Rgba(10, 20, 30, 255));
        byte[] png = TestImages.EncodePng(src);

        var loaded = PngLoader.Load(png);

        Assert.Equal(2, loaded.Width);
        Assert.Equal(2, loaded.Height);
        Assert.Equal(new Rgba(10, 20, 30, 255), loaded.At(0, 0));
        Assert.Equal((byte)0, loaded.At(1, 0).A); // transparent pixel
    }

    [Fact]
    public void Load_InvalidBytes_ThrowsInvalidPngException()
    {
        Assert.Throws<InvalidPngException>(
            () => PngLoader.Load(new byte[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public void IsPng_RealPng_True()
    {
        byte[] png = TestImages.EncodePng(TestImages.FromAscii(new[] { "#" }, new Rgba(1, 2, 3, 255)));
        Assert.True(PngLoader.IsPng(png));
    }

    [Fact]
    public void IsPng_Svg_False()
    {
        // A plausible SVG upload — starts with "<?xml"/"<svg", never the PNG signature.
        byte[] svg = System.Text.Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
        Assert.False(PngLoader.IsPng(svg));
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E })] // truncated signature
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0 })] // JPEG magic, 8 bytes
    public void IsPng_NonPng_False(byte[] bytes) => Assert.False(PngLoader.IsPng(bytes));

    [Fact]
    public void IsPng_NullOrShort_False()
    {
        Assert.False(PngLoader.IsPng(null));
        Assert.False(PngLoader.IsPng(Array.Empty<byte>()));
    }
}
