using System.Collections.Generic;
using Bunit;
using Mixel.Web.Components;
using Mixel.Web.Services;
using Xunit;

public class FilePanelTests : BunitContext
{
    [Fact]
    public void Renders_Spritesheets_Section()
    {
        var cut = Render<FilePanel>(p => p
            .Add(x => x.Items, new List<FileItem>())
            .Add(x => x.Sheets, new List<SpritesheetItem>()));

        Assert.Contains("Spritesheets", cut.Markup);
        Assert.Contains("Add Spritesheet", cut.Markup);
    }

    [Fact]
    public void Renders_Slice_Button_Per_Sheet()
    {
        var sheets = new List<SpritesheetItem>
        {
            new() { Name = "hero.png", Bytes = new byte[] { 1 } }
        };
        var cut = Render<FilePanel>(p => p
            .Add(x => x.Items, new List<FileItem>())
            .Add(x => x.Sheets, sheets));

        Assert.Contains("hero.png", cut.Markup);
        Assert.Contains("Slice →", cut.Markup);
    }
}
