using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

namespace Mixel.Core;

using VERTEX = VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>;

public static class GltfWriter
{
    private static ModelRoot BuildModel(Mesh mesh, byte[] pngTexture)
    {
        var material = new MaterialBuilder("mixel")
            .WithMetallicRoughnessShader()
            .WithDoubleSide(true)
            .WithMetallicRoughness(0f, 1f)
            .WithBaseColor(ImageBuilder.From(new MemoryImage(pngTexture)), new Vector4(1, 1, 1, 1));

        // Crisp pixels: NEAREST min & mag, clamp to edge.
        // API note: in SharpGLTF 1.0.6 WithSampler param order is (wrapS, wrapT, mipMap, magFilter).
        // The brief had (mipMap, magFilter, wrapS, wrapT) — order corrected here.
        // Using UseChannel(KnownChannel) to avoid the [Obsolete] GetChannel(string) overload.
        material.UseChannel(KnownChannel.BaseColor).Texture!.WithSampler(
            TextureWrapMode.CLAMP_TO_EDGE,
            TextureWrapMode.CLAMP_TO_EDGE,
            TextureMipMapFilter.NEAREST,
            TextureInterpolationFilter.NEAREST);

        var mb = VERTEX.CreateCompatibleMesh("mixel");
        var prim = mb.UsePrimitive(material);

        for (int i = 0; i < mesh.Indices.Count; i += 3)
            prim.AddTriangle(V(mesh, mesh.Indices[i]), V(mesh, mesh.Indices[i + 1]), V(mesh, mesh.Indices[i + 2]));

        var scene = new SceneBuilder();
        scene.AddRigidMesh(mb, Matrix4x4.Identity);
        return scene.ToGltf2();
    }

    private static VERTEX V(Mesh mesh, int i)
    {
        var p = new Vector3(mesh.Positions[i * 3], mesh.Positions[i * 3 + 1], mesh.Positions[i * 3 + 2]);
        var n = new Vector3(mesh.Normals[i * 3], mesh.Normals[i * 3 + 1], mesh.Normals[i * 3 + 2]);
        var uv = new Vector2(mesh.Uvs[i * 2], mesh.Uvs[i * 2 + 1]);
        return new VERTEX(new VertexPositionNormal(p, n), new VertexTexture1(uv));
    }

    public static byte[] WriteGlbBytes(Mesh mesh, byte[] pngTexture)
    {
        var model = BuildModel(mesh, pngTexture);
        var seg = model.WriteGLB();
        return seg.ToArray();
    }

    public static void Write(Mesh mesh, byte[] pngTexture, GltfFormat format, string outputPath)
    {
        var model = BuildModel(mesh, pngTexture);
        switch (format)
        {
            case GltfFormat.Glb:
                model.SaveGLB(outputPath);
                break;
            case GltfFormat.Gltf:
                model.SaveGLTF(outputPath); // .gltf + satellite .bin + .png
                break;
            case GltfFormat.GltfEmbedded:
                model.SaveGLTF(outputPath, new WriteSettings
                {
                    ImageWriting = ResourceWriteMode.EmbeddedAsBase64,
                    MergeBuffers = true,
                });
                break;
        }
    }

    public static IReadOnlyList<MixelFile> WriteToMemory(
        Mesh mesh, byte[] pngTexture, GltfFormat format, string baseName)
    {
        var model = BuildModel(mesh, pngTexture);

        switch (format)
        {
            case GltfFormat.Glb:
                return new[] { new MixelFile($"{baseName}.glb", model.WriteGLB().ToArray()) };

            case GltfFormat.GltfEmbedded:
            {
                // Single self-contained .gltf with base64-embedded image + buffer.
                // SharpGLTF 1.0.6 note: WriteToDictionary does not exist. Use WriteContext.CreateFromDictionary.
                // EmbeddedAsBase64 embeds images as base64 data URIs, but binary buffers still go as satellite .bin.
                // To produce a truly single-file .gltf we post-process: replace the satellite .bin uri reference
                // in the JSON with a base64 data URI (valid per glTF 2.0 spec, §3.6.1.3).
                var dict = new Dictionary<string, ArraySegment<byte>>();
                var ctx = WriteContext.CreateFromDictionary(dict);
                ctx.ImageWriting = ResourceWriteMode.EmbeddedAsBase64;
                ctx.MergeBuffers = true;
                ctx.WriteTextSchema2(baseName, model);

                // Inline any satellite .bin files into the gltf JSON as base64 data URIs.
                var gltfKey = $"{baseName}.gltf";
                if (dict.TryGetValue(gltfKey, out var gltfSeg))
                {
                    var json = Encoding.UTF8.GetString(gltfSeg.Array!, gltfSeg.Offset, gltfSeg.Count);
                    foreach (var key in dict.Keys.Where(k => k.EndsWith(".bin", StringComparison.Ordinal)))
                    {
                        var binSeg = dict[key];
                        var b64 = Convert.ToBase64String(binSeg.Array!, binSeg.Offset, binSeg.Count);
                        var dataUri = $"data:application/octet-stream;base64,{b64}";
                        json = json.Replace($"\"uri\":\"{key}\"", $"\"uri\":\"{dataUri}\"");
                    }
                    return new[] { new MixelFile(gltfKey, Encoding.UTF8.GetBytes(json)) };
                }

                // Fallback: should not be reached, but return all files rather than crash.
                return dict.Select(kv => new MixelFile(kv.Key, kv.Value.ToArray())).ToList();
            }

            case GltfFormat.Gltf:
            default:
            {
                var dict = new Dictionary<string, ArraySegment<byte>>();
                var ctx = WriteContext.CreateFromDictionary(dict);
                ctx.WriteTextSchema2(baseName, model);
                return dict.Select(kv => new MixelFile(kv.Key, kv.Value.ToArray())).ToList();
            }
        }
    }
}
