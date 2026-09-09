using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Mixel.Web.Components;
using Mixel.Web.Interop;
using Mixel.Web.Theming;
using Xunit;

public class ThemePickerTests : BunitContext
{
    private IRenderedComponent<ThemePicker> RenderPicker()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped<MixelJs>();
        Services.AddScoped<ThemeState>();
        return Render<ThemePicker>();
    }

    [Fact]
    public void Renders_TwoSegments_LightAndDark()
    {
        var cut = RenderPicker();
        var segs = cut.FindAll(".theme-toggle .tt-seg");
        Assert.Equal(2, segs.Count);
        Assert.Equal("Light", segs[0].TextContent);
        Assert.Equal("Dark", segs[1].TextContent);
    }

    [Fact]
    public void DefaultTheme_DarkSideIsActive()
    {
        // ThemeState default is vaporsoft-dark → the Dark segment carries the active (carmine) class.
        var cut = RenderPicker();
        var segs = cut.FindAll(".theme-toggle .tt-seg");
        Assert.DoesNotContain("on", segs[0].ClassList); // Light not active
        Assert.Contains("on", segs[1].ClassList);       // Dark active
    }

    [Fact]
    public void ClickingLight_MovesActiveSideToLight()
    {
        var cut = RenderPicker();
        cut.FindAll(".theme-toggle .tt-seg")[0].Click(); // Light

        var segs = cut.FindAll(".theme-toggle .tt-seg");
        Assert.Contains("on", segs[0].ClassList);        // Light now active
        Assert.DoesNotContain("on", segs[1].ClassList);  // Dark no longer active
    }
}
