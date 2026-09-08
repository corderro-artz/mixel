using System.Linq;
using Mixel.Core;
using Mixel.Web.Services;
using Xunit;

public class SlicerStateTests
{
    private static RgbaImage Sheet(int w = 4, int h = 2)
    {
        var px = new Rgba[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Rgba(9, 9, 9, 255);
        return new RgbaImage { Width = w, Height = h, Pixels = px };
    }

    [Fact]
    public void TryAdd_AutoNames_Sequentially()
    {
        var s = new SlicerState();
        Assert.True(s.TryAdd(new SpriteRect(0, 0, 1, 1), out _));
        Assert.True(s.TryAdd(new SpriteRect(2, 0, 1, 1), out _));
        Assert.Equal(new[] { "sprite_01", "sprite_02" }, s.Regions.Select(r => r.Name));
    }

    [Fact]
    public void TryAdd_Reuses_Lowest_Free_Name_After_Remove()
    {
        var s = new SlicerState();
        s.TryAdd(new SpriteRect(0, 0, 1, 1), out _);
        s.TryAdd(new SpriteRect(1, 0, 1, 1), out _);
        s.TryAdd(new SpriteRect(2, 0, 1, 1), out _);
        s.Remove(s.Regions.First(r => r.Name == "sprite_02"));
        s.TryAdd(new SpriteRect(3, 0, 1, 1), out _);
        Assert.Contains(s.Regions, r => r.Name == "sprite_02");
        Assert.Equal(3, s.Regions.Count);
    }

    [Fact]
    public void TryAdd_Rejects_Overlap()
    {
        var s = new SlicerState();
        s.TryAdd(new SpriteRect(0, 0, 2, 2), out _);
        var ok = s.TryAdd(new SpriteRect(1, 1, 2, 2), out var error);
        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Single(s.Regions);
    }

    [Fact]
    public void TryAdd_Allows_Flush_Adjacent()
    {
        var s = new SlicerState();
        s.TryAdd(new SpriteRect(0, 0, 2, 2), out _);
        Assert.True(s.TryAdd(new SpriteRect(2, 0, 2, 2), out _));
    }

    [Fact]
    public void TryAdd_Rejects_Empty()
    {
        var s = new SlicerState();
        Assert.False(s.TryAdd(new SpriteRect(0, 0, 0, 0), out _));
        Assert.Empty(s.Regions);
    }

    [Fact]
    public void Rename_Trims_And_Falls_Back_When_Blank()
    {
        var s = new SlicerState();
        s.TryAdd(new SpriteRect(0, 0, 1, 1), out _);
        var r = s.Regions[0];
        s.Rename(r, "  hero  ");
        Assert.Equal("hero", r.Name);
        s.Rename(r, "   ");
        Assert.StartsWith("sprite_", r.Name);
    }

    [Fact]
    public void IsStandardSize_Detects_PowerOfTwo()
    {
        var s = new SlicerState();
        s.TryAdd(new SpriteRect(0, 0, 16, 16), out _);
        s.TryAdd(new SpriteRect(20, 0, 3, 3), out _);
        Assert.True(s.IsStandardSize(s.Regions[0]));
        Assert.False(s.IsStandardSize(s.Regions[1]));
    }

    [Fact]
    public void BuildExtract_Produces_One_Png_Per_Region_With_Png_Names()
    {
        var s = new SlicerState();
        s.TryAdd(new SpriteRect(0, 0, 2, 2), out _);
        s.TryAdd(new SpriteRect(2, 0, 2, 2), out _);
        var outFiles = s.BuildExtract(Sheet());
        Assert.Equal(2, outFiles.Count);
        Assert.Equal("sprite_01.png", outFiles[0].name);
        var img = PngLoader.Load(outFiles[0].bytes);
        Assert.Equal(2, img.Width);
        Assert.Equal(2, img.Height);
    }

    [Fact]
    public void BuildExtract_Dedupes_Duplicate_Names()
    {
        var s = new SlicerState();
        s.TryAdd(new SpriteRect(0, 0, 2, 2), out _);
        s.TryAdd(new SpriteRect(2, 0, 2, 2), out _);
        s.Rename(s.Regions[0], "hero");
        s.Rename(s.Regions[1], "hero");
        var names = s.BuildExtract(Sheet()).Select(f => f.name).ToArray();
        Assert.Equal(new[] { "hero.png", "hero_2.png" }, names);
    }
}
