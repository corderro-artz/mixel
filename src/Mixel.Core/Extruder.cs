using System.Collections.Generic;

namespace Mixel.Core;

public static class Extruder
{
    private static (Mesh mesh, byte[] tex) Build(ExtrudeOptions o)
    {
        var img = PngLoader.Load(o.PngBytes);
        var mask = SilhouetteMask.Build(img);
        var tex = TextureBaker.BakePng(img, mask);
        var mesh = MeshBuilder.Build(mask, o.Depth, o.VoxelSize, o.Pivot);
        return (mesh, tex);
    }

    public static byte[] ExtrudeGlb(ExtrudeOptions o)
    {
        var (mesh, tex) = Build(o);
        return GltfWriter.WriteGlbBytes(mesh, tex);
    }

    public static void ExtrudeToFile(ExtrudeOptions o, string outputPath)
    {
        var (mesh, tex) = Build(o);
        GltfWriter.Write(mesh, tex, o.Format, outputPath);
    }

    public static IReadOnlyList<MixelFile> ExtrudeToMemory(ExtrudeOptions o, string baseName)
    {
        var (mesh, tex) = Build(o);
        return GltfWriter.WriteToMemory(mesh, tex, o.Format, baseName);
    }

    public static string DefaultExtension(GltfFormat f)
        => f == GltfFormat.Glb ? ".glb" : ".gltf";
}
