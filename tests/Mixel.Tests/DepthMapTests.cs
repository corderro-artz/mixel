using System;
using Mixel.Core;
using Xunit;

public class DepthMapTests
{
    private static DepthMap Make(byte[] levels, int w, int h) => new()
    {
        Width = w, Height = h, Levels = levels, OffsetX = 0, OffsetY = 0
    };

    [Fact]
    public void At_InBounds_ReturnsCorrectLevel()
    {
        var dm = Make(new byte[] { 0, 1, 2, 3 }, w: 2, h: 2);
        Assert.Equal((byte)0, dm.At(0, 0));
        Assert.Equal((byte)1, dm.At(1, 0));
        Assert.Equal((byte)2, dm.At(0, 1));
        Assert.Equal((byte)3, dm.At(1, 1));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    public void At_OutOfBounds_ReturnsZero(int x, int y)
    {
        var dm = Make(new byte[] { 1, 1, 1, 1 }, w: 2, h: 2);
        Assert.Equal((byte)0, dm.At(x, y));
    }

    [Fact]
    public void MaxLevel_ReturnsMaxInLevels()
    {
        var dm = Make(new byte[] { 0, 3, 1, 2 }, w: 2, h: 2);
        Assert.Equal(3, dm.MaxLevel);
    }

    [Fact]
    public void MaxLevel_EmptyLevels_ReturnsZero()
    {
        var dm = Make(Array.Empty<byte>(), w: 0, h: 0);
        Assert.Equal(0, dm.MaxLevel);
    }
}
