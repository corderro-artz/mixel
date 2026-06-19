namespace Mixel.Core;

public sealed class EmptySilhouetteException : Exception
{
    public EmptySilhouetteException()
        : base("Image contains no fully-opaque pixels to extrude.") { }
}

public sealed class Mask
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required bool[] Solid { get; init; } // row-major over cropped bbox
    public required int OffsetX { get; init; }  // bbox origin in source image
    public required int OffsetY { get; init; }

    public bool At(int x, int y)
        => x >= 0 && y >= 0 && x < Width && y < Height && Solid[y * Width + x];
}

public static class SilhouetteMask
{
    public static Mask Build(RgbaImage image)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;

        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (image.At(x, y).A == 255)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }

        if (maxX < 0) throw new EmptySilhouetteException();

        int w = maxX - minX + 1, h = maxY - minY + 1;
        var solid = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                solid[y * w + x] = image.At(minX + x, minY + y).A == 255;

        return new Mask { Width = w, Height = h, Solid = solid, OffsetX = minX, OffsetY = minY };
    }
}
