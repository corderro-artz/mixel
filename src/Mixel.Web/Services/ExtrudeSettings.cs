using Mixel.Core;

namespace Mixel.Web.Services;

public sealed class ExtrudeSettings
{
    public int Depth { get; set; } = 1;
    public double VoxelSize { get; set; } = 1.0;
    public GltfFormat Format { get; set; } = GltfFormat.Glb;
    public Pivot Pivot { get; set; } = Pivot.BottomCenter;
    public bool AllowNonStandardSize { get; set; } = false;
    public bool PerPixelMode   { get; set; } = false;
    public int  MaxDepthLevels { get; set; } = 16;
    public ExtrudeMode ExtrudeMode { get; set; } = ExtrudeMode.Front;

    public ExtrudeOptions ToOptions(byte[] png) => new()
    {
        PngBytes = png,
        Depth = Depth,
        VoxelSize = (float)VoxelSize,
        Format = Format,
        Pivot = Pivot,
        AllowNonStandardSize = AllowNonStandardSize,
        Mode = ExtrudeMode,
    };

    public ExtrudeOptions ToOptions(byte[] png, FileItem? item)
    {
        if (PerPixelMode && item?.DepthLevels != null)
        {
            return new ExtrudeOptions
            {
                PngBytes = png,
                VoxelSize = (float)VoxelSize,
                Format = Format,
                Pivot = Pivot,
                AllowNonStandardSize = AllowNonStandardSize,
                Mode = ExtrudeMode,
                DepthMap = new DepthMap
                {
                    Width   = item.DepthWidth,
                    Height  = item.DepthHeight,
                    Levels  = item.DepthLevels,
                },
            };
        }
        return ToOptions(png);
    }
}
