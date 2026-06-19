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

    /// <summary>
    /// Named-risk check: verify the hand-rolled embedded .gltf round-trips through SharpGLTF
    /// and contains no external .bin or .png URI references.
    /// </summary>
    [Fact]
    public void GltfEmbedded_IsActuallySelfContained_RoundTrips()
    {
        var files = Extruder.ExtrudeToMemory(Opts(GltfFormat.GltfEmbedded), "hero");

        // Must be exactly one file.
        Assert.Single(files);
        var gltfFile = files[0];

        // Decode JSON and check no external file URIs remain.
        var json = System.Text.Encoding.UTF8.GetString(gltfFile.Bytes);
        Assert.DoesNotContain("hero.bin", json);
        Assert.DoesNotContain("hero.png", json);
        // All buffer URIs must be data URIs.
        // Confirm "uri" entries (if any) are data: URIs.
        // We look for any "uri":"<something>" that does NOT start with "data:".
        var uriPattern = new System.Text.RegularExpressions.Regex(
            "\"uri\"\\s*:\\s*\"(?!data:)([^\"]+)\"");
        var externalUris = uriPattern.Matches(json);
        Assert.Empty(externalUris); // No external file references allowed.

        // Round-trip parse: build a temp directory with just the single file, then parse.
        // SharpGLTF's ReadSchema2 / ParseGltf from bytes. Try via a temp file.
        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mixel_embed_{Guid.NewGuid():N}.gltf");
        try
        {
            System.IO.File.WriteAllBytes(tmp, gltfFile.Bytes);
            var model = ModelRoot.Load(tmp);
            Assert.Single(model.LogicalMeshes);
            Assert.NotEmpty(model.LogicalTextures);

            // Verify buffer byteLength consistency:
            // The declared byteLength in the JSON must match the actual decoded byte count.
            var bufNode = System.Text.Json.JsonDocument.Parse(json)
                .RootElement.GetProperty("buffers")[0];
            var declaredByteLength = bufNode.GetProperty("byteLength").GetInt32();
            var dataUriValue = bufNode.GetProperty("uri").GetString()!;
            // data:application/octet-stream;base64,<b64>
            var b64 = dataUriValue.Substring(dataUriValue.IndexOf(',') + 1);
            var actualBytes = Convert.FromBase64String(b64);
            Assert.Equal(declaredByteLength, actualBytes.Length);
        }
        finally
        {
            if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
        }
    }
}
