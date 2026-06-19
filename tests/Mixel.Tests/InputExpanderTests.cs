using System.IO;
using System.Linq;
using Mixel.Core;
using Xunit;

public class InputExpanderTests
{
    [Fact]
    public void Expand_Directory_FindsTopLevelPngsSorted()
    {
        string dir = Directory.CreateTempSubdirectory("mixel_in_").FullName;
        File.WriteAllBytes(Path.Combine(dir, "b.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(dir, "a.png"), new byte[] { 1 });
        File.WriteAllText(Path.Combine(dir, "note.txt"), "skip me");
        string sub = Directory.CreateDirectory(Path.Combine(dir, "sub")).FullName;
        File.WriteAllBytes(Path.Combine(sub, "c.png"), new byte[] { 1 });

        var top = InputExpander.Expand(new[] { dir }, recursive: false);
        Assert.Equal(2, top.Count);
        Assert.EndsWith("a.png", top[0]);
        Assert.EndsWith("b.png", top[1]);

        var all = InputExpander.Expand(new[] { dir }, recursive: true);
        Assert.Equal(3, all.Count); // includes sub/c.png
    }

    [Fact]
    public void Expand_MissingPath_Throws()
    {
        Assert.Throws<FileNotFoundException>(
            () => InputExpander.Expand(new[] { "does_not_exist.png" }, false));
    }
}
