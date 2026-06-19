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
}
