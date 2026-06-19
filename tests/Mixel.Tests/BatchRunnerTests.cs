using System.IO;
using Mixel.Core;
using Xunit;

public class BatchRunnerTests
{
    [Fact]
    public void Run_OneBadImage_DoesNotAbortBatch()
    {
        string dir = Directory.CreateTempSubdirectory("mixel_batch_").FullName;
        string good = Path.Combine(dir, "good.png");
        string bad = Path.Combine(dir, "bad.png");
        File.WriteAllBytes(good,
            TestImages.EncodePng(TestImages.FromAscii(new[] { "#" }, new Rgba(1, 2, 3, 255))));
        File.WriteAllBytes(bad, new byte[] { 9, 9, 9 }); // not a PNG

        string outDir = Directory.CreateTempSubdirectory("mixel_out_").FullName;
        var result = BatchRunner.Run(
            new[] { good, bad }, depth: 1, voxelSize: 1f,
            GltfFormat.Glb, Pivot.BottomCenter, outDir, allowNonStandardSize: true);

        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);
        Assert.True(File.Exists(Path.Combine(outDir, "good.glb")));
    }
}
