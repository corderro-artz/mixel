using Mixel.Core;
using Xunit;

public class RgbaImageTests
{
    [Fact]
    public void At_ReturnsPixelByRowMajorIndex()
    {
        var img = new RgbaImage
        {
            Width = 2,
            Height = 2,
            Pixels = new[]
            {
                new Rgba(1, 0, 0, 255), new Rgba(2, 0, 0, 255),
                new Rgba(3, 0, 0, 255), new Rgba(4, 0, 0, 255),
            }
        };

        Assert.Equal(new Rgba(1, 0, 0, 255), img.At(0, 0));
        Assert.Equal(new Rgba(2, 0, 0, 255), img.At(1, 0));
        Assert.Equal(new Rgba(3, 0, 0, 255), img.At(0, 1));
        Assert.Equal(new Rgba(4, 0, 0, 255), img.At(1, 1));
    }
}
