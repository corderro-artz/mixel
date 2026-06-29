namespace Mixel.Core;

public sealed record ExtrudeOptions
{
    public required byte[] PngBytes { get; init; }
    public int Depth { get; init; } = 1;
    public float VoxelSize { get; init; } = 1f;
    public GltfFormat Format { get; init; } = GltfFormat.Glb;
    public Pivot Pivot { get; init; } = Pivot.BottomCenter;

    /// <summary>When false (default), non-standard image sizes are rejected.
    /// See <see cref="ImageSize"/>.</summary>
    public bool AllowNonStandardSize { get; init; } = false;

    /// <summary>When non-null, activates per-pixel depth mode. Overrides <see cref="Depth"/>.</summary>
    public DepthMap? DepthMap { get; init; } = null;
}
