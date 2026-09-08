using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Mixel.Web.Components;
using Mixel.Web.Interop;
using Xunit;

public class SlicerDialogTests : BunitContext
{
    private void Setup()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped<MixelJs>();
    }

    [Fact]
    public void Renders_Header_And_Disabled_Confirm_For_Valid_Sheet()
    {
        Setup();
        var png = PngFixture.Solid(16, 16, 200, 100, 50);
        var cut = Render<SlicerDialog>(p => p
            .Add(x => x.SheetBytes, png)
            .Add(x => x.SheetName, "sheet.png"));

        Assert.NotNull(cut.Find("[data-test=slicer-overlay]"));
        var confirm = cut.Find("[data-test=slicer-confirm]");
        Assert.True(confirm.HasAttribute("disabled"));
    }

    [Fact]
    public void Renders_Error_State_For_Invalid_Png()
    {
        Setup();
        var cut = Render<SlicerDialog>(p => p
            .Add(x => x.SheetBytes, new byte[] { 1, 2, 3, 4 })
            .Add(x => x.SheetName, "broken.png"));

        Assert.NotNull(cut.Find("[data-test=slicer-error]"));
    }
}
