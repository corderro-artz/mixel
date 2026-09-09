using System;
using System.IO;
using StbImageSharp;

namespace Mixel.Core;

public sealed class InvalidPngException : Exception
{
    public InvalidPngException(string message) : base(message) { }
}

public static class PngLoader
{
    // PNG files always begin with this 8-byte signature (see the PNG spec §5.2).
    private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// True only if <paramref name="bytes"/> starts with the PNG signature. Cheap gate to
    /// reject non-PNG uploads (SVG, JPG, etc.) before any decode is attempted.
    /// </summary>
    public static bool IsPng(byte[]? bytes)
    {
        if (bytes is null || bytes.Length < Signature.Length) return false;
        for (int i = 0; i < Signature.Length; i++)
            if (bytes[i] != Signature[i]) return false;
        return true;
    }

    public static RgbaImage Load(byte[] bytes)
    {
        ImageResult img;
        try
        {
            img = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex)
        {
            throw new InvalidPngException($"Could not decode PNG: {ex.Message}");
        }

        if (img is null || img.Data is null || img.Width <= 0 || img.Height <= 0)
            throw new InvalidPngException("Decoded image was empty.");

        var px = new Rgba[img.Width * img.Height];
        for (int i = 0; i < px.Length; i++)
        {
            int b = i * 4;
            px[i] = new Rgba(img.Data[b], img.Data[b + 1], img.Data[b + 2], img.Data[b + 3]);
        }

        return new RgbaImage { Width = img.Width, Height = img.Height, Pixels = px };
    }

    public static RgbaImage Load(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Load(ms.ToArray());
    }
}
