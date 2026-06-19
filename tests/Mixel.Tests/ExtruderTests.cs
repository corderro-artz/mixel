using System;
using Mixel.Core;
using SharpGLTF.Schema2;
using Xunit;

public class ExtruderTests
{
    [Fact]
    public void ExtrudeGlb_RoundTripsThroughPngBytes()
    {
        var img = TestImages.FromAscii(new[] { "##", "##" }, new Rgba(0, 128, 255, 255));
        var opts = new ExtrudeOptions
        {
            PngBytes = TestImages.EncodePng(img),
            Depth = 2,
            VoxelSize = 1f,
            Pivot = Pivot.MinCorner,
            AllowNonStandardSize = true,
        };

        byte[] glb = Extruder.ExtrudeGlb(opts);
        var model = ModelRoot.ParseGLB(new ArraySegment<byte>(glb));

        Assert.Single(model.LogicalMeshes);
    }

    [Fact]
    public void ExtrudeGlb_EmptyImage_ThrowsEmptySilhouette()
    {
        var img = new RgbaImage { Width = 1, Height = 1, Pixels = new[] { new Rgba(0, 0, 0, 0) } };
        var opts = new ExtrudeOptions { PngBytes = TestImages.EncodePng(img), AllowNonStandardSize = true };

        Assert.Throws<EmptySilhouetteException>(() => Extruder.ExtrudeGlb(opts));
    }
}
