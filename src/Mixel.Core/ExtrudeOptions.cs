namespace Mixel.Core;

public sealed record ExtrudeOptions
{
    public required byte[] PngBytes { get; init; }
    public int Depth { get; init; } = 1;
    public float VoxelSize { get; init; } = 1f;
    public GltfFormat Format { get; init; } = GltfFormat.Glb;
    public Pivot Pivot { get; init; } = Pivot.BottomCenter;
}
