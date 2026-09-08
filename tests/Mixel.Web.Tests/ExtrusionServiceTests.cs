using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Mixel.Core;
using Mixel.Web.Services;
using SharpGLTF.Schema2;
using Xunit;

public class ExtrusionServiceTests
{
    private static readonly ExtrudeSettings Default = new() { AllowNonStandardSize = true };

    [Fact]
    public void Single_Glb_ReturnsOneNamedFile()
    {
        var files = ExtrusionService.Single(PngFixture.Solid(2, 2, 0, 0, 0), "hero", Default);
        Assert.Single(files);
        Assert.Equal("hero.glb", files[0].Name);
    }

    [Fact]
    public void BatchZip_GoodAndBad_ZipsGood_ReportsBad()
    {
        var inputs = new (string name, byte[] png, FileItem? item)[]
        {
            ("good.png", PngFixture.Solid(2, 2, 1, 2, 3), null),
            ("bad.png", new byte[] { 9, 9, 9 }, null), // not a PNG
        };

        var zip = ExtrusionService.BatchZip(inputs, Default, out var outcomes);

        Assert.Equal(1, outcomes.Count(o => o.Success));
        Assert.Equal(1, outcomes.Count(o => !o.Success));
        using var za = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        Assert.Contains(za.Entries, e => e.FullName == "good.glb");
        Assert.DoesNotContain(za.Entries, e => e.FullName.StartsWith("bad"));
    }

}
