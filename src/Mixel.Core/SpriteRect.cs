namespace Mixel.Core;

public readonly record struct SpriteRect(int X, int Y, int Width, int Height)
{
    public int Right  => X + Width;
    public int Bottom => Y + Height;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Edges that merely touch do not count as intersecting.</summary>
    public bool Intersects(SpriteRect o)
        => X < o.Right && Right > o.X && Y < o.Bottom && Bottom > o.Y;

    public bool WithinBounds(int imageWidth, int imageHeight)
        => X >= 0 && Y >= 0 && Width > 0 && Height > 0
           && Right <= imageWidth && Bottom <= imageHeight;
}
