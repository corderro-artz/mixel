using System;
using System.Linq;
using Mixel.Core;
using SharpGLTF.Schema2;
using Xunit;

public class ExtruderDepthMapTests
{
    private static byte[] TwoPng() =>
        TestImages.EncodePng(TestImages.FromAscii(new[] { "##", "##" }, new Rgba(200, 100, 50, 255)));

    private static DepthMap FlatDM(int w, int h, byte level) => new()
    {
        Width = w, Height = h, OffsetX = 0, OffsetY = 0,
        Levels = Enumerable.Repeat(level, w * h).Select(x => (byte)x).ToArray()
    };

    [Fact]
    public void ExtrudeGlb_WithDepthMap_ProducesValidGlb()
    {
        var opts = new ExtrudeOptions
        {
            PngBytes = TwoPng(),
            AllowNonStandardSize = true,
            DepthMap = FlatDM(2, 2, 2),
        };
        var glb = Extruder.ExtrudeGlb(opts);
        var model = ModelRoot.ParseGLB(new ArraySegment<byte>(glb));
        Assert.Single(model.LogicalMeshes);
    }

    [Fact]
    public void ExtrudeGlb_WithNullDepthMap_UsesSimplePath()
    {
        var opts = new ExtrudeOptions
        {
            PngBytes = TwoPng(),
            AllowNonStandardSize = true,
            Depth = 2,
            DepthMap = null,
        };
        var glb = Extruder.ExtrudeGlb(opts);
        Assert.NotEmpty(glb);
    }

    [Fact]
    public void ExtrudeToMemory_WithDepthMap_Glb_ReturnsNamedFile()
    {
        var opts = new ExtrudeOptions
        {
            PngBytes = TwoPng(),
            AllowNonStandardSize = true,
            Format = GltfFormat.Glb,
            DepthMap = FlatDM(2, 2, 1),
        };
        var files = Extruder.ExtrudeToMemory(opts, "hero");
        Assert.Single(files);
        Assert.Equal("hero.glb", files[0].Name);
    }
}
