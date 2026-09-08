using System.IO.Compression;
using Mixel.Core;

namespace Mixel.Web.Services;

public sealed record BatchOutcome(string Input, bool Success, string? Error);

public static class ExtrusionService
{
    public static IReadOnlyList<MixelFile> Single(byte[] png, string baseName, ExtrudeSettings s, FileItem? item = null)
        => Extruder.ExtrudeToMemory(s.ToOptions(png, item), baseName);

    public static byte[] Zip(IReadOnlyList<Mixel.Core.MixelFile> files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var f in files)
            {
                var e = zip.CreateEntry(f.Name, CompressionLevel.Optimal);
                using var es = e.Open();
                es.Write(f.Bytes, 0, f.Bytes.Length);
            }
        return ms.ToArray();
    }

    public static byte[] BatchZip(
        IReadOnlyList<(string name, byte[] png, FileItem? item)> inputs, ExtrudeSettings s,
        out IReadOnlyList<BatchOutcome> outcomes)
    {
        var results = new List<BatchOutcome>(inputs.Count);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, png, item) in inputs)
            {
                var baseName = Path.GetFileNameWithoutExtension(name);
                try
                {
                    foreach (var file in Extruder.ExtrudeToMemory(s.ToOptions(png, item), baseName))
                    {
                        var entry = zip.CreateEntry(file.Name, CompressionLevel.Optimal);
                        using var es = entry.Open();
                        es.Write(file.Bytes, 0, file.Bytes.Length);
                    }
                    results.Add(new BatchOutcome(name, true, null));
                }
                catch (Exception ex)
                {
                    results.Add(new BatchOutcome(name, false, ex.Message));
                }
            }
        }
        outcomes = results;
        return ms.ToArray();
    }
}
