using System.Linq;
using Mixel.Web.Theming;
using Xunit;

public class ThemeRegistryTests
{
    [Fact]
    public void Has14Themes_SevenDarkSevenLight()
    {
        Assert.Equal(14, ThemeRegistry.All.Count);
        Assert.Equal(7, ThemeRegistry.All.Count(t => t.Mode == ThemeMode.Dark));
        Assert.Equal(7, ThemeRegistry.All.Count(t => t.Mode == ThemeMode.Light));
    }

    [Fact]
    public void DefaultIsVaporsoftDark()
    {
        Assert.Equal("vaporsoft-dark", ThemeRegistry.DefaultId);
        var def = ThemeRegistry.ById(ThemeRegistry.DefaultId);
        Assert.Equal(ThemeMode.Dark, def.Mode);
        Assert.True(def.Brand);
    }

    [Fact]
    public void BrandPairPresent()
    {
        Assert.Contains(ThemeRegistry.All, t => t.Id == "vaporsoft-dark" && t.Brand);
        Assert.Contains(ThemeRegistry.All, t => t.Id == "vaporsoft-light" && t.Brand);
    }

    [Fact]
    public void IdsAreUnique()
    {
        Assert.Equal(ThemeRegistry.All.Count, ThemeRegistry.All.Select(t => t.Id).Distinct().Count());
    }
}
