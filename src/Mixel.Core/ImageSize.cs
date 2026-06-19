using System;

namespace Mixel.Core;

/// <summary>Thrown when an input image's dimensions are not a standard voxel size
/// and non-standard sizing was not explicitly allowed.</summary>
public sealed class InvalidImageSizeException : Exception
{
    public InvalidImageSizeException(string message) : base(message) { }
}

/// <summary>
/// Standard sizes are powers of two per dimension, from 8 up to 1024
/// (8, 16, 32, 64, 128, 256, 512, 1024). Width and height are validated
/// independently, so non-square images such as 16x32 are accepted.
/// </summary>
public static class ImageSize
{
    public const int Min = 8;
    public const int Max = 1024;

    public static bool IsPowerOfTwoInRange(int n)
        => n >= Min && n <= Max && (n & (n - 1)) == 0;

    public static bool IsStandard(int width, int height)
        => IsPowerOfTwoInRange(width) && IsPowerOfTwoInRange(height);

    /// <summary>Throws <see cref="InvalidImageSizeException"/> for non-standard sizes
    /// unless <paramref name="allowNonStandard"/> is set.</summary>
    public static void Validate(int width, int height, bool allowNonStandard)
    {
        if (allowNonStandard || IsStandard(width, height)) return;
        throw new InvalidImageSizeException(
            $"image size {width}x{height} is non-standard; each dimension must be a power of two " +
            $"from {Min} to {Max} (8, 16, 32, …, 1024). Pass the non-standard sizing flag to allow it.");
    }
}
