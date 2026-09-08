using StbImageWriteSharp;

namespace Mixel.Core;

public static class SpritesheetSlicer
{
    public static byte[] Crop(RgbaImage source, SpriteRect rect)
    {
        if (!rect.WithinBounds(source.Width, source.Height))
            throw new ArgumentOutOfRangeException(nameof(rect),
                $"rect {rect} is outside image bounds {source.Width}x{source.Height}");

        int w = rect.Width, h = rect.Height;
        var data = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = source.At(rect.X + x, rect.Y + y);
                int b = (y * w + x) * 4;
                data[b] = p.R; data[b + 1] = p.G; data[b + 2] = p.B; data[b + 3] = p.A;
            }

        using var ms = new MemoryStream();
        new ImageWriter().WritePng(data, w, h, ColorComponents.RedGreenBlueAlpha, ms);
        return ms.ToArray();
    }
}
