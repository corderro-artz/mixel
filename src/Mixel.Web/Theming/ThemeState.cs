using Mixel.Web.Interop;

namespace Mixel.Web.Theming;

public sealed class ThemeState
{
    private readonly MixelJs _js;
    public ThemeState(MixelJs js) => _js = js;

    public string CurrentId { get; private set; } = ThemeRegistry.DefaultId;
    public event Action? Changed;

    /// <summary>
    /// Pure resolution logic (no I/O):
    /// - Known stored id → stored
    /// - stored is null → prefers dark ? "vaporsoft-dark" : "vaporsoft-light"
    /// - Unknown stored id (non-null) → ThemeRegistry.DefaultId
    /// </summary>
    public static string ResolveInitial(string? stored, bool prefersDark)
    {
        if (stored is null)
            return prefersDark ? "vaporsoft-dark" : "vaporsoft-light";

        if (ThemeRegistry.All.Any(t => t.Id == stored))
            return stored;

        // Non-null but unknown: fall back to the hard default
        return ThemeRegistry.DefaultId;
    }

    public async Task InitAsync()
    {
        var stored = await _js.LoadThemeAsync();
        var prefersDark = await _js.PrefersDarkAsync();
        CurrentId = ResolveInitial(stored, prefersDark);
        Changed?.Invoke();
    }

    public async Task SetAsync(string id)
    {
        CurrentId = ThemeRegistry.ById(id).Id;
        await _js.SaveThemeAsync(CurrentId);
        Changed?.Invoke();
    }
}
