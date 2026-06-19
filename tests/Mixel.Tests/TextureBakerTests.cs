using Mixel.Core;
using Xunit;

public class TextureBakerTests
{
    [Fact]
    public void BakePng_CropsToMaskBoundingBox()
    {
        var src = TestImages.FromAscii(new[]
        {
            "....",
            ".#..",
            "....",
        }, new Rgba(10, 20, 30, 255));
        var mask = SilhouetteMask.Build(src); // 1x1 at offset (1,1)

        byte[] png = TextureBaker.BakePng(src, mask);
        var baked = PngLoader.Load(png);

        Assert.Equal(1, baked.Width);
        Assert.Equal(1, baked.Height);
        Assert.Equal(new Rgba(10, 20, 30, 255), baked.At(0, 0));
    }
}
