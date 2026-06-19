namespace Mixel.Web.Theming;

public static class ThemeRegistry
{
    public const string DefaultId = "vaporsoft-dark";

    public static IReadOnlyList<Theme> All { get; } = new[]
    {
        new Theme("vaporsoft-dark",  "Vaporsoft Dark",  ThemeMode.Dark,  true),
        new Theme("vaporsoft-light", "Vaporsoft Light", ThemeMode.Light, true),
        new Theme("midnight",  "Midnight",  ThemeMode.Dark,  false),
        new Theme("carbon",    "Carbon",    ThemeMode.Dark,  false),
        new Theme("forest",    "Forest",    ThemeMode.Dark,  false),
        new Theme("ember",     "Ember",     ThemeMode.Dark,  false),
        new Theme("synthwave", "Synthwave", ThemeMode.Dark,  false),
        new Theme("ocean",     "Ocean",     ThemeMode.Dark,  false),
        new Theme("daylight",  "Daylight",  ThemeMode.Light, false),
        new Theme("linen",     "Linen",     ThemeMode.Light, false),
        new Theme("mint",      "Mint",      ThemeMode.Light, false),
        new Theme("rose",      "Rose",      ThemeMode.Light, false),
        new Theme("slate",     "Slate",     ThemeMode.Light, false),
        new Theme("sand",      "Sand",      ThemeMode.Light, false),
    };

    public static Theme ById(string id)
        => All.FirstOrDefault(t => t.Id == id) ?? All[0];
}
