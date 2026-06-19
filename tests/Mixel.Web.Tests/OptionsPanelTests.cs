using Bunit;
using Mixel.Core;
using Mixel.Web.Components;
using Mixel.Web.Services;
using Xunit;

public class OptionsPanelTests : BunitContext
{
    [Fact]
    public void IncreasingDepth_UpdatesSettings_AndRaisesOnChanged()
    {
        var settings = new ExtrudeSettings(); // Depth defaults to 1
        var raised = false;
        var cut = Render<OptionsPanel>(p => p
            .Add(c => c.Settings, settings)
            .Add(c => c.OnChanged, () => raised = true));

        cut.Find("[data-test='depth'] button[aria-label='Increase depth']").Click();

        Assert.Equal(2, settings.Depth);
        Assert.True(raised);
    }

    [Fact]
    public void Depth_CannotExceedMax()
    {
        var settings = new ExtrudeSettings { Depth = 3 };
        var cut = Render<OptionsPanel>(p => p.Add(c => c.Settings, settings));

        var inc = cut.Find("[data-test='depth'] button[aria-label='Increase depth']");
        Assert.True(inc.HasAttribute("disabled"));
    }

    [Fact]
    public void SelectingFormat_UpdatesSettings()
    {
        var settings = new ExtrudeSettings();
        var cut = Render<OptionsPanel>(p => p.Add(c => c.Settings, settings));
        cut.Find("[data-test='format'] input[value='Gltf']").Change("Gltf");
        Assert.Equal(GltfFormat.Gltf, settings.Format);
    }

    [Fact]
    public void SelectingPivot_UpdatesSettings()
    {
        var settings = new ExtrudeSettings();
        var cut = Render<OptionsPanel>(p => p.Add(c => c.Settings, settings));
        cut.Find("[data-test='pivot'] input[value='Center']").Change("Center");
        Assert.Equal(Pivot.Center, settings.Pivot);
    }
}
