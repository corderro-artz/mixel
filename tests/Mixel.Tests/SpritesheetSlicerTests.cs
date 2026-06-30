using System;
using Mixel.Core;
using Xunit;

public class SpritesheetSlicerTests
{
    private static readonly Rgba Solid = new(10, 20, 30, 255);

    private static RgbaImage Sheet() => TestImages.FromAscii(
        new[]
        {
            "#.#.",
            "..##",
        }, Solid);

    [Fact]
    public void Crop_Produces_Png_Of_Rect_Dimensions()
    {
        var png = SpritesheetSlicer.Crop(Sheet(), new SpriteRect(2, 0, 2, 2));
        var img = PngLoader.Load(png);
        Assert.Equal(2, img.Width);
        Assert.Equal(2, img.Height);
    }

    [Fact]
    public void Crop_RoundTrips_Pixels_From_Region()
    {
        var png = SpritesheetSlicer.Crop(Sheet(), new SpriteRect(2, 0, 2, 2));
        var img = PngLoader.Load(png);
        Assert.Equal(Solid, img.At(0, 0));
        Assert.Equal((byte)0, img.At(1, 0).A);
        Assert.Equal(Solid, img.At(0, 1));
        Assert.Equal(Solid, img.At(1, 1));
    }

    [Fact]
    public void Crop_OnePixel_Works()
    {
        var png = SpritesheetSlicer.Crop(Sheet(), new SpriteRect(0, 0, 1, 1));
        var img = PngLoader.Load(png);
        Assert.Equal(1, img.Width);
        Assert.Equal(1, img.Height);
        Assert.Equal(Solid, img.At(0, 0));
    }

    [Fact]
    public void Crop_FullImage_Works()
    {
        var png = SpritesheetSlicer.Crop(Sheet(), new SpriteRect(0, 0, 4, 2));
        var img = PngLoader.Load(png);
        Assert.Equal(4, img.Width);
        Assert.Equal(2, img.Height);
    }

    [Fact]
    public void Crop_OutOfBounds_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => SpritesheetSlicer.Crop(Sheet(), new SpriteRect(3, 0, 2, 2)));
}
