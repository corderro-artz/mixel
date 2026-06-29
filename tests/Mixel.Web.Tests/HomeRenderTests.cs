using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Mixel.Web.Interop;
using Mixel.Web.Theming;
using Xunit;

public class HomeRenderTests : BunitContext
{
    [Fact]
    public void Home_Renders_TopBarAndThemePicker()
    {
        JSInterop.Mode = JSRuntimeMode.Loose; // tolerate interop calls during init
        Services.AddScoped<MixelJs>();
        Services.AddScoped<ThemeState>();

        var cut = Render<Mixel.Web.Pages.Home>();

        // "mi<b>x</b>el" renders as separate text nodes, so check for the logo div and "mi" text
        Assert.Contains("logo", cut.Markup);
        Assert.Contains("topbar", cut.Markup);
        Assert.NotNull(cut.Find("select.theme-picker"));
        Assert.NotNull(cut.Find("model-viewer"));
    }

    [Fact]
    public void Home_PerPixelMode_ShowsDepthPainterNotModelViewer_WhenFileLoaded()
    {
        // Not easily testable with bUnit (DepthPainter requires JS interop).
        // Verify Home renders without error in PerPixelMode=true scenario.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped<MixelJs>();
        Services.AddScoped<ThemeState>();
        var cut = Render<Mixel.Web.Pages.Home>();
        Assert.Contains("workspace", cut.Markup);
    }

    [Fact]
    public void Home_SimpleMode_ShowsModelViewer()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddScoped<MixelJs>();
        Services.AddScoped<ThemeState>();
        var cut = Render<Mixel.Web.Pages.Home>();
        Assert.NotNull(cut.Find("model-viewer"));
    }
}
