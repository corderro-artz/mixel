namespace Mixel.Core;

public sealed record BatchItemResult(string InputPath, bool Success, string? Error, string? OutputPath);

public sealed record BatchResult(IReadOnlyList<BatchItemResult> Items)
{
    public int SucceededCount => Items.Count(i => i.Success);
    public int FailedCount => Items.Count(i => !i.Success);
}

public static class BatchRunner
{
    public static BatchResult Run(
        IReadOnlyList<string> pngPaths,
        int depth, float voxelSize, GltfFormat format, Pivot pivot, string? outputDir)
    {
        var items = new List<BatchItemResult>(pngPaths.Count);
        string ext = Extruder.DefaultExtension(format);

        foreach (var path in pngPaths)
        {
            try
            {
                var opts = new ExtrudeOptions
                {
                    PngBytes = File.ReadAllBytes(path),
                    Depth = depth,
                    VoxelSize = voxelSize,
                    Format = format,
                    Pivot = pivot,
                };

                string dir = outputDir ?? Path.GetDirectoryName(Path.GetFullPath(path))!;
                Directory.CreateDirectory(dir);
                string outPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + ext);

                Extruder.ExtrudeToFile(opts, outPath);
                items.Add(new BatchItemResult(path, true, null, outPath));
            }
            catch (Exception ex)
            {
                items.Add(new BatchItemResult(path, false, ex.Message, null));
            }
        }

        return new BatchResult(items);
    }
}
