using Mixel.Core;
using Xunit;

public class SpriteRectTests
{
    [Fact]
    public void Right_And_Bottom_Are_Exclusive_Edges()
    {
        var r = new SpriteRect(2, 3, 4, 5);
        Assert.Equal(6, r.Right);
        Assert.Equal(8, r.Bottom);
    }

    [Fact]
    public void IsEmpty_True_When_Zero_Or_Negative_Dimension()
    {
        Assert.True(new SpriteRect(0, 0, 0, 4).IsEmpty);
        Assert.True(new SpriteRect(0, 0, 4, 0).IsEmpty);
        Assert.False(new SpriteRect(0, 0, 1, 1).IsEmpty);
    }

    [Fact]
    public void Intersects_True_When_Overlapping()
        => Assert.True(new SpriteRect(0, 0, 4, 4).Intersects(new SpriteRect(2, 2, 4, 4)));

    [Fact]
    public void Intersects_False_When_Flush_Adjacent()
        => Assert.False(new SpriteRect(0, 0, 4, 4).Intersects(new SpriteRect(4, 0, 4, 4)));

    [Fact]
    public void Intersects_False_When_Disjoint()
        => Assert.False(new SpriteRect(0, 0, 2, 2).Intersects(new SpriteRect(8, 8, 2, 2)));

    [Fact]
    public void Intersects_True_When_Contained()
        => Assert.True(new SpriteRect(0, 0, 10, 10).Intersects(new SpriteRect(3, 3, 2, 2)));

    [Theory]
    [InlineData(0, 0, 16, 16, true)]
    [InlineData(8, 8, 8, 8, true)]
    [InlineData(0, 0, 17, 16, false)]
    [InlineData(-1, 0, 4, 4, false)]
    [InlineData(0, 0, 0, 4, false)]
    public void WithinBounds_Validates_Against_Image(int x, int y, int w, int h, bool expected)
        => Assert.Equal(expected, new SpriteRect(x, y, w, h).WithinBounds(16, 16));
}
