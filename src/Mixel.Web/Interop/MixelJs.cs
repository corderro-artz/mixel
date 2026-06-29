using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Mixel.Web.Interop;

public sealed class MixelJs
{
    private readonly IJSRuntime _js;
    public MixelJs(IJSRuntime js) => _js = js;

    public ValueTask SetModelSrcAsync(ElementReference el, byte[] bytes)
        => _js.InvokeVoidAsync("mixel.setModelSrc", el, bytes);

    public ValueTask DownloadFileAsync(string name, byte[] bytes)
        => _js.InvokeVoidAsync("mixel.downloadFile", name, bytes);

    public ValueTask SaveThemeAsync(string id)
        => _js.InvokeVoidAsync("mixel.saveTheme", id);

    public ValueTask<string?> LoadThemeAsync()
        => _js.InvokeAsync<string?>("mixel.loadTheme");

    public ValueTask<bool> PrefersDarkAsync()
        => _js.InvokeAsync<bool>("mixel.prefersDark");

    public ValueTask SetThemeAttributeAsync(string id)
        => _js.InvokeVoidAsync("mixel.setThemeAttribute", id);

    public ValueTask InitDepthCanvasAsync(string id, byte[] rgbaBytes, int w, int h)
        => _js.InvokeVoidAsync("mixel.initDepthCanvas", id, rgbaBytes, w, h);

    public ValueTask RenderDepthOverlayAsync(string id, byte[] levels, int w, int h, int maxDepth)
        => _js.InvokeVoidAsync("mixel.renderDepthOverlay", id, levels, w, h, maxDepth);
}
