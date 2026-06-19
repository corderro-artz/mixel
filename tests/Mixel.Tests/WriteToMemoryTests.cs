using System;
using System.Linq;
using Mixel.Core;
using SharpGLTF.Schema2;
using Xunit;

public class WriteToMemoryTests
{
    private static ExtrudeOptions Opts(GltfFormat fmt) => new()
    {
        PngBytes = TestImages.EncodePng(TestImages.FromAscii(new[] { "##", "##" }, new Rgba(10, 120, 200, 255))),
        Depth = 2, VoxelSize = 1f, Format = fmt, Pivot = Pivot.MinCorner,
    };

    [Fact]
    public void Glb_ReturnsSingleParseableGlb()
    {
        var files = Extruder.ExtrudeToMemory(Opts(GltfFormat.Glb), "hero");
        Assert.Single(files);
        Assert.Equal("hero.glb", files[0].Name);
        var model = ModelRoot.ParseGLB(new ArraySegment<byte>(files[0].Bytes));
        Assert.Single(model.LogicalMeshes);
    }

    [Fact]
    public void GltfEmbedded_ReturnsSingleSelfContainedGltf()
    {
        var files = Extruder.ExtrudeToMemory(Opts(GltfFormat.GltfEmbedded), "hero");
        Assert.Single(files);
        Assert.Equal("hero.gltf", files[0].Name);
        Assert.True(files[0].Bytes.Length > 0);
    }

    [Fact]
    public void Gltf_ReturnsGltfPlusSatellites()
    {
        var files = Extruder.ExtrudeToMemory(Opts(GltfFormat.Gltf), "hero");
        Assert.Contains(files, f => f.Name == "hero.gltf");
        Assert.True(files.Count >= 2); // .gltf + at least one of .bin/.png
        Assert.All(files, f => Assert.True(f.Bytes.Length > 0));
    }
}
