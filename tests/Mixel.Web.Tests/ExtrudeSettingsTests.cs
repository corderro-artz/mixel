using Mixel.Core;
using Mixel.Web.Services;
using Xunit;

public class ExtrudeSettingsTests
{
    [Fact]
    public void ToOptions_CopiesAllFields()
    {
        var s = new ExtrudeSettings { Depth = 4, VoxelSize = 2.5, Format = GltfFormat.GltfEmbedded, Pivot = Pivot.Center };
        var png = new byte[] { 1, 2, 3 };

        var o = s.ToOptions(png);

        Assert.Same(png, o.PngBytes);
        Assert.Equal(4, o.Depth);
        Assert.Equal(2.5f, o.VoxelSize);
        Assert.Equal(GltfFormat.GltfEmbedded, o.Format);
        Assert.Equal(Pivot.Center, o.Pivot);
    }

    [Fact]
    public void ToOptions_WithItem_PerPixelFalse_NullDepthMap()
    {
        var s = new ExtrudeSettings { PerPixelMode = false };
        var item = new FileItem { Name = "a.png", Bytes = new byte[0],
            DepthLevels = new byte[] { 1, 2 }, DepthWidth = 2, DepthHeight = 1 };
        var o = s.ToOptions(new byte[0], item);
        Assert.Null(o.DepthMap);
    }

    [Fact]
    public void ToOptions_WithItem_PerPixelTrue_NullLevels_FallsBack()
    {
        var s = new ExtrudeSettings { PerPixelMode = true };
        var item = new FileItem { Name = "a.png", Bytes = new byte[0], DepthLevels = null };
        var o = s.ToOptions(new byte[0], item);
        Assert.Null(o.DepthMap);
    }

    [Fact]
    public void ToOptions_WithItem_PerPixelTrue_ValidLevels_BuildsDepthMap()
    {
        var levels = new byte[] { 1, 2, 3, 1 };
        var s = new ExtrudeSettings { PerPixelMode = true };
        var item = new FileItem { Name = "a.png", Bytes = new byte[0],
            DepthLevels = levels, DepthWidth = 4, DepthHeight = 1 };
        var o = s.ToOptions(new byte[0], item);
        Assert.NotNull(o.DepthMap);
        Assert.Equal(4, o.DepthMap!.Width);
        Assert.Equal(1, o.DepthMap.Height);
        Assert.Same(levels, o.DepthMap.Levels);
    }

    [Fact]
    public void ToOptions_WithNullItem_PerPixelTrue_FallsBack()
    {
        var s = new ExtrudeSettings { PerPixelMode = true };
        var o = s.ToOptions(new byte[0], null);
        Assert.Null(o.DepthMap);
    }

    [Fact]
    public void MaxDepthLevels_DefaultIs16()
    {
        Assert.Equal(16, new ExtrudeSettings().MaxDepthLevels);
    }

    [Fact]
    public void PerPixelMode_DefaultIsFalse()
    {
        Assert.False(new ExtrudeSettings().PerPixelMode);
    }
}
