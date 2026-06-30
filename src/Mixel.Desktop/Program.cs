using System.Diagnostics;

var exeDir = Path.GetDirectoryName(Environment.ProcessPath)
             ?? Directory.GetCurrentDirectory();

// Blazor WASM publish leaves #[.{fingerprint}] tokens in index.html unresolved.
// Patch them once using the actual fingerprinted filenames in _framework/.
PatchIndexHtml(Path.Combine(exeDir, "wwwroot"));

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args            = args,
    ContentRootPath = exeDir,
    WebRootPath     = Path.Combine(exeDir, "wwwroot"),
});

var app = builder.Build();

app.MapStaticAssets();
app.MapFallbackToFile("index.html");

const string url = "http://localhost:5037";

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
{
    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    catch { /* best-effort */ }
});

app.Run(url);

static void PatchIndexHtml(string wwwroot)
{
    var indexHtml = Path.Combine(wwwroot, "index.html");
    if (!File.Exists(indexHtml)) return;
    var html = File.ReadAllText(indexHtml);
    if (!html.Contains("#[.{fingerprint}]")) return;   // already patched or not needed

    var fwDir = Path.Combine(wwwroot, "_framework");
    if (!Directory.Exists(fwDir)) return;

    foreach (var file in Directory.EnumerateFiles(fwDir, "*.js")
                         .Where(f => !f.EndsWith(".gz") && !f.EndsWith(".br")))
    {
        var name   = Path.GetFileName(file);          // e.g. blazor.webassembly.958z1vx7fr.js
        var parts  = name.Split('.');
        if (parts.Length < 3) continue;
        var ext         = parts[^1];                  // js
        var logicalBase = string.Join('.', parts[..^2]); // blazor.webassembly
        var placeholder = $"{logicalBase}#[.{{fingerprint}}].{ext}";
        html = html.Replace(placeholder, name);
    }

    File.WriteAllText(indexHtml, html);
}
