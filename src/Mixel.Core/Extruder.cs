using System.Collections.Generic;

namespace Mixel.Core;

public static class Extruder
{
    private static (Mesh mesh, byte[] tex) Build(ExtrudeOptions o)
    {
        var img  = PngLoader.Load(o.PngBytes);
        ImageSize.Validate(img.Width, img.Height, o.AllowNonStandardSize);
        Mask mask;
        if (o.DepthMap is not null)
        {
            // Per-pixel mode: use full-image mask so UVs from BuildFromDepthMap align.
            // Use A > 0 to match EnsureDepthLevels and avoid EmptySilhouetteException
            // on semi-transparent images.
            var solid = new bool[img.Width * img.Height];
            for (int i = 0; i < img.Pixels.Length; i++)
                solid[i] = img.Pixels[i].A > 0;
            mask = new Mask { Width = img.Width, Height = img.Height, Solid = solid, OffsetX = 0, OffsetY = 0 };
        }
        else
        {
            mask = SilhouetteMask.Build(img);
        }
        var tex  = TextureBaker.BakePng(img, mask);
        var mesh = o.DepthMap is not null
            ? MeshBuilder.BuildFromDepthMap(o.DepthMap, o.VoxelSize, o.Pivot, o.Mode)
            : MeshBuilder.Build(mask, o.Depth, o.VoxelSize, o.Pivot, o.Mode);
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
