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
}
