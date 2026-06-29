using System.Linq;

namespace Mixel.Core;

public sealed class DepthMap
{
    public required int Width   { get; init; }
    public required int Height  { get; init; }
    /// <summary>Row-major. 0 = air, 1..N = depth level.</summary>
    public required byte[] Levels { get; init; }
    public required int OffsetX { get; init; }
    public required int OffsetY { get; init; }

    public byte At(int x, int y)
        => x >= 0 && y >= 0 && x < Width && y < Height
            ? Levels[y * Width + x] : (byte)0;

    public int MaxLevel => Levels.Length == 0 ? 0 : Levels.Max();
}
