namespace Mixel.Web.Services;

public sealed class FileItem
{
    public required string Name { get; init; }
    public required byte[] Bytes { get; init; }
    public bool Selected { get; set; } = true;

    /// <summary>Null until per-pixel mode activates. 0=air, 1..N=depth level.</summary>
    public byte[]? DepthLevels { get; set; }
    public int DepthWidth      { get; set; }
    public int DepthHeight     { get; set; }

    /// <summary>True for pixels that are solid (non-transparent) in the source PNG. Set alongside DepthLevels.</summary>
    public bool[]? SolidMask   { get; set; }

    /// <summary>Raw RGBA bytes for the canvas, computed once alongside DepthLevels to avoid decoding the PNG twice.</summary>
    public byte[]? RgbaBytes   { get; set; }
}
