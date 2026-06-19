using Mixel.Core;
using Xunit;

public class SilhouetteMaskTests
{
    private static readonly Rgba S = new(255, 255, 255, 255);

    [Fact]
    public void Build_CropsToBoundingBox_OpaqueOnly()
    {
        // 4x4 with a 2x2 opaque block at (1,1)-(2,2), rest transparent.
        var img = TestImages.FromAscii(new[]
        {
            "....",
            ".##.",
            ".##.",
            "....",
        }, S);

        var mask = SilhouetteMask.Build(img);

        Assert.Equal(2, mask.Width);
        Assert.Equal(2, mask.Height);
        Assert.Equal(1, mask.OffsetX);
        Assert.Equal(1, mask.OffsetY);
        Assert.True(mask.At(0, 0));
        Assert.True(mask.At(1, 1));
        Assert.False(mask.At(-1, 0)); // out of range
        Assert.False(mask.At(2, 0));
    }

    [Fact]
    public void Build_TreatsPartialAlphaAsEmpty()
    {
        var img = new RgbaImage
        {
            Width = 2, Height = 1,
            Pixels = new[] { new Rgba(255, 0, 0, 254), new Rgba(0, 255, 0, 255) }
        };

        var mask = SilhouetteMask.Build(img);

        Assert.Equal(1, mask.Width);  // only the alpha==255 pixel survives
        Assert.Equal(1, mask.OffsetX);
        Assert.True(mask.At(0, 0));
    }

    [Fact]
    public void Build_AllTransparent_Throws()
    {
        var img = new RgbaImage
        {
            Width = 1, Height = 1, Pixels = new[] { new Rgba(0, 0, 0, 0) }
        };

        Assert.Throws<EmptySilhouetteException>(() => SilhouetteMask.Build(img));
    }
}
