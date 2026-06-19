using Mixel.Web.Theming;
using Xunit;

public class ThemeStateTests
{
    [Fact]
    public void Stored_TakesPrecedence()
    {
        Assert.Equal("ocean", ThemeState.ResolveInitial("ocean", prefersDark: true));
        Assert.Equal("mint", ThemeState.ResolveInitial("mint", prefersDark: false));
    }

    [Fact]
    public void NoStored_PrefersDark_PicksVaporsoftDark()
    {
        Assert.Equal("vaporsoft-dark", ThemeState.ResolveInitial(null, prefersDark: true));
    }

    [Fact]
    public void NoStored_PrefersLight_PicksVaporsoftLight()
    {
        Assert.Equal("vaporsoft-light", ThemeState.ResolveInitial(null, prefersDark: false));
    }

    [Fact]
    public void UnknownStoredId_FallsBackToDefault()
    {
        Assert.Equal("vaporsoft-dark", ThemeState.ResolveInitial("does-not-exist", prefersDark: false));
    }
}
