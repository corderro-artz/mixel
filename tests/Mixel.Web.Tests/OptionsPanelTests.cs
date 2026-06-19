using Bunit;
using Mixel.Core;
using Mixel.Web.Components;
using Mixel.Web.Services;
using Xunit;

public class OptionsPanelTests : BunitContext
{
    [Fact]
    public void EditingDepth_UpdatesSettings_AndRaisesOnChanged()
    {
        var settings = new ExtrudeSettings();
        var raised = false;
        var cut = Render<OptionsPanel>(p => p
            .Add(c => c.Settings, settings)
            .Add(c => c.OnChanged, () => raised = true));

        var depth = cut.Find("input[data-test='depth']");
        depth.Change("5");

        Assert.Equal(5, settings.Depth);
        Assert.True(raised);
    }

    [Fact]
    public void SelectingFormat_UpdatesSettings()
    {
        var settings = new ExtrudeSettings();
        var cut = Render<OptionsPanel>(p => p.Add(c => c.Settings, settings));
        cut.Find("select[data-test='format']").Change("Gltf");
        Assert.Equal(GltfFormat.Gltf, settings.Format);
    }
}
