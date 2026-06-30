using System.Collections.Generic;
using Bunit;
using Mixel.Web.Components;
using Mixel.Web.Services;
using Xunit;

public class FilePanelTests : BunitContext
{
    [Fact]
    public void Renders_Spritesheet_Slicer_Entry()
    {
        var cut = Render<FilePanel>(p => p
            .Add(x => x.Items, new List<FileItem>()));

        Assert.Contains("Slice a spritesheet", cut.Markup);
    }
}
