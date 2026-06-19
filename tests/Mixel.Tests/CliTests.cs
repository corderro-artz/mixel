using System;
using System.IO;
using Mixel.Core;
using Xunit;

public class CliTests
{
    [Fact]
    public void Run_SingleImage_WritesGlb_ReturnsZero()
    {
        string dir = Directory.CreateTempSubdirectory("mixel_cli_").FullName;
        string input = Path.Combine(dir, "hero.png");
        File.WriteAllBytes(input,
            TestImages.EncodePng(TestImages.FromAscii(new[] { "##", "##" }, new Rgba(9, 9, 9, 255))));
        string output = Path.Combine(dir, "hero.glb");

        int code = Program.Run(
            new[] { input }, output, depth: 2, voxelSize: 1.0,
            format: "glb", pivot: "bottom-center", recursive: false);

        Assert.Equal(0, code);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public void Run_MissingInput_ReturnsTwo()
    {
        int code = Program.Run(
            new[] { "nope.png" }, null, 1, 1.0, "glb", "bottom-center", false);
        Assert.Equal(2, code);
    }

    [Fact]
    public void Run_BadFlagValue_ReturnsFive()
    {
        string dir = Directory.CreateTempSubdirectory("mixel_cli_").FullName;
        string input = Path.Combine(dir, "x.png");
        File.WriteAllBytes(input,
            TestImages.EncodePng(TestImages.FromAscii(new[] { "#" }, new Rgba(1, 1, 1, 255))));

        int code = Program.Run(new[] { input }, null, depth: 0, voxelSize: 1.0,
            format: "glb", pivot: "bottom-center", recursive: false); // depth < 1
        Assert.Equal(5, code);
    }
}
