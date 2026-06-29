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
        var settings = new ExtrudeSettings(); // Depth defaults to 1, PerPixelMode=false
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

    [Fact]
    public void ModeToggle_ClickPerPixel_SetsPerPixelMode()
    {
        var settings = new ExtrudeSettings();
        var cut = Render<OptionsPanel>(p => p.Add(c => c.Settings, settings));
        cut.Find("[data-test='mode-perpixel']").Click();
        Assert.True(settings.PerPixelMode);
    }

    [Fact]
    public void ModeToggle_ClickSimple_ClearsPerPixelMode()
    {
        var settings = new ExtrudeSettings { PerPixelMode = true };
        var cut = Render<OptionsPanel>(p => p.Add(c => c.Settings, settings));
        cut.Find("[data-test='mode-simple']").Click();
        Assert.False(settings.PerPixelMode);
    }

    [Fact]
    public void SimpleMode_ShowsDepthStepper_HidesMaxDepthSelect()
    {
        var settings = new ExtrudeSettings { PerPixelMode = false };
        var cut = Render<OptionsPanel>(p => p.Add(c => c.Settings, settings));
        Assert.NotNull(cut.Find("[data-test='depth']"));
        Assert.Empty(cut.FindAll("[data-test='max-depth-levels']"));
    }

    [Fact]
    public void PerPixelMode_HidesDepthStepper_ShowsMaxDepthSelect()
    {
        var settings = new ExtrudeSettings { PerPixelMode = true };
        var cut = Render<OptionsPanel>(p => p.Add(c => c.Settings, settings));
        Assert.Empty(cut.FindAll("[data-test='depth']"));
        Assert.NotNull(cut.Find("[data-test='max-depth-levels']"));
    }

    [Fact]
    public void MaxDepthLevels_Select_HasOptions_8_16_24_32()
    {
        var settings = new ExtrudeSettings { PerPixelMode = true };
        var cut = Render<OptionsPanel>(p => p.Add(c => c.Settings, settings));
        var options = cut.FindAll("[data-test='max-depth-levels'] option");
        Assert.Equal(4, options.Count);
        Assert.Equal("8",  options[0].GetAttribute("value"));
        Assert.Equal("16", options[1].GetAttribute("value"));
        Assert.Equal("24", options[2].GetAttribute("value"));
        Assert.Equal("32", options[3].GetAttribute("value"));
    }

    [Fact]
    public void VoxelSize_NotRendered()
    {
        var settings = new ExtrudeSettings();
        var cut = Render<OptionsPanel>(p => p.Add(c => c.Settings, settings));
        Assert.Empty(cut.FindAll("[data-test='voxel']"));
    }
}
