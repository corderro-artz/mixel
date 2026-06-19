using System;
using Mixel.Core;
using SharpGLTF.Schema2;
using Xunit;

public class GltfWriterTests
{
    private static (Mixel.Core.Mesh, byte[]) BuildSquare()
    {
        var img = TestImages.FromAscii(new[] { "#" }, new Rgba(200, 50, 50, 255));
        var mask = SilhouetteMask.Build(img);
        var mesh = MeshBuilder.Build(mask, 1, 1f, Pivot.MinCorner);
        var tex = TextureBaker.BakePng(img, mask);
        return (mesh, tex);
    }

    [Fact]
    public void WriteGlbBytes_ProducesParseableGlbWithOneMeshAndTexture()
    {
        var (mesh, tex) = BuildSquare();

        byte[] glb = GltfWriter.WriteGlbBytes(mesh, tex);
        var model = ModelRoot.ParseGLB(new ArraySegment<byte>(glb));

        Assert.Single(model.LogicalMeshes);
        Assert.NotEmpty(model.LogicalTextures);
        // NEAREST sampler (mag filter 9728).
        var sampler = model.LogicalTextureSamplers[0];
        Assert.Equal(TextureInterpolationFilter.NEAREST, sampler.MagFilter);
    }

    [Theory]
    [InlineData(GltfFormat.Glb, ".glb")]
    [InlineData(GltfFormat.Gltf, ".gltf")]
    [InlineData(GltfFormat.GltfEmbedded, ".gltf")]
    public void Write_CreatesFileForEachFormat(GltfFormat fmt, string ext)
    {
        var (mesh, tex) = BuildSquare();
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"mixel_test_{Guid.NewGuid():N}{ext}");

        GltfWriter.Write(mesh, tex, fmt, path);

        Assert.True(System.IO.File.Exists(path));
        Assert.True(new System.IO.FileInfo(path).Length > 0);
    }
}
