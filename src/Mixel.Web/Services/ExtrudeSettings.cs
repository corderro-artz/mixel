using Mixel.Core;

namespace Mixel.Web.Services;

public sealed class ExtrudeSettings
{
    public int Depth { get; set; } = 1;
    public double VoxelSize { get; set; } = 1.0;
    public GltfFormat Format { get; set; } = GltfFormat.Glb;
    public Pivot Pivot { get; set; } = Pivot.BottomCenter;
    public bool AllowNonStandardSize { get; set; } = false;

    public ExtrudeOptions ToOptions(byte[] png) => new()
    {
        PngBytes = png,
        Depth = Depth,
        VoxelSize = (float)VoxelSize,
        Format = Format,
        Pivot = Pivot,
        AllowNonStandardSize = AllowNonStandardSize,
    };
}
