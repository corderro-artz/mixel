using System.IO;
using StbImageWriteSharp;

public static class PngFixture
{
    // A w x h fully-opaque PNG of one color.
    public static byte[] Solid(int w, int h, byte r, byte g, byte b)
    {
        var data = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            data[i * 4 + 0] = r; data[i * 4 + 1] = g; data[i * 4 + 2] = b; data[i * 4 + 3] = 255;
        }
        using var ms = new MemoryStream();
        new ImageWriter().WritePng(data, w, h, ColorComponents.RedGreenBlueAlpha, ms);
        return ms.ToArray();
    }
}
