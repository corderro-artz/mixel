using Mixel.Core;
using Xunit;

public class ImageSizeTests
{
    [Theory]
    [InlineData(8, 8)]
    [InlineData(16, 16)]
    [InlineData(512, 512)]
    [InlineData(1024, 1024)]
    [InlineData(16, 32)]   // non-square but each dim a power of two
    [InlineData(1024, 8)]
    public void Standard_Sizes_AreAccepted(int w, int h)
    {
        Assert.True(ImageSize.IsStandard(w, h));
        ImageSize.Validate(w, h, allowNonStandard: false); // does not throw
    }

    [Theory]
    [InlineData(4, 4)]     // below min
    [InlineData(2048, 2048)] // above max
    [InlineData(24, 24)]   // multiple of 8 but not a power of two
    [InlineData(10, 16)]   // one dim invalid
    [InlineData(1, 1)]
    public void NonStandard_Sizes_AreRejected(int w, int h)
    {
        Assert.False(ImageSize.IsStandard(w, h));
        Assert.Throws<InvalidImageSizeException>(() => ImageSize.Validate(w, h, allowNonStandard: false));
    }

    [Theory]
    [InlineData(24, 24)]
    [InlineData(1, 1)]
    public void AllowNonStandard_BypassesValidation(int w, int h)
    {
        ImageSize.Validate(w, h, allowNonStandard: true); // does not throw
    }

    [Fact]
    public void Extruder_RejectsNonStandardSize_ByDefault()
    {
        // 2x2 is a power of two per dimension but below the 8px minimum.
        var img = TestImages.FromAscii(new[] { "##", "##" }, new Rgba(1, 2, 3, 255));
        var opts = new ExtrudeOptions { PngBytes = TestImages.EncodePng(img) };
        Assert.Throws<InvalidImageSizeException>(() => Extruder.ExtrudeGlb(opts));
    }
}
