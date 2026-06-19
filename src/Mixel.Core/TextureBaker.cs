using StbImageWriteSharp;

namespace Mixel.Core;

public static class TextureBaker
{
    public static byte[] BakePng(RgbaImage source, Mask mask)
    {
        int w = mask.Width, h = mask.Height;
        var data = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = source.At(mask.OffsetX + x, mask.OffsetY + y);
                int b = (y * w + x) * 4;
                data[b + 0] = p.R; data[b + 1] = p.G; data[b + 2] = p.B; data[b + 3] = p.A;
            }

        using var ms = new MemoryStream();
        new ImageWriter().WritePng(data, w, h, ColorComponents.RedGreenBlueAlpha, ms);
        return ms.ToArray();
    }
}
