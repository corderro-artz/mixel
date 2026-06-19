using System.IO;
using Mixel.Core;
using StbImageWriteSharp;

public static class TestImages
{
    // Encode an RgbaImage to PNG bytes using StbImageWriteSharp.
    public static byte[] EncodePng(RgbaImage img)
    {
        var data = new byte[img.Width * img.Height * 4];
        for (int i = 0; i < img.Pixels.Length; i++)
        {
            var p = img.Pixels[i];
            data[i * 4 + 0] = p.R;
            data[i * 4 + 1] = p.G;
            data[i * 4 + 2] = p.B;
            data[i * 4 + 3] = p.A;
        }

        using var ms = new MemoryStream();
        new ImageWriter().WritePng(
            data, img.Width, img.Height,
            StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, ms);
        return ms.ToArray();
    }

    public static RgbaImage Solid1x1(Rgba color) => new()
    {
        Width = 1,
        Height = 1,
        Pixels = new[] { color }
    };

    // Build an RgbaImage from a string grid: '#' = given solid color (alpha 255),
    // '.' = fully transparent. All rows must be equal length.
    public static RgbaImage FromAscii(string[] rows, Rgba solid)
    {
        int h = rows.Length, w = rows[0].Length;
        var px = new Rgba[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = rows[y][x] == '#' ? solid : new Rgba(0, 0, 0, 0);
        return new RgbaImage { Width = w, Height = h, Pixels = px };
    }
}
