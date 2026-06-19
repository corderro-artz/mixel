namespace Mixel.Core;

public readonly record struct Rgba(byte R, byte G, byte B, byte A);

public sealed class RgbaImage
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required Rgba[] Pixels { get; init; } // row-major, length Width*Height

    public Rgba At(int x, int y) => Pixels[y * Width + x];
}
