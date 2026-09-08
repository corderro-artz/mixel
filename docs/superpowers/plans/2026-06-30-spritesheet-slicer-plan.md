# Spritesheet Slicer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user upload a spritesheet PNG, draw named rectangular selections over it, and extract each selection into the existing editor as an ordinary `FileItem`.

**Architecture:** Core gains a pure `SpriteRect` value type and a `SpritesheetSlicer.Crop` function (PNG crop via the existing `StbImageWriteSharp` encoder). Web gains a JS-free `SlicerState` logic class (mirroring `DepthPainterState`), a `SlicerDialog.razor` modal that wires canvas/pointer JS to that state, new `mixel.js` slicer functions, and minimal wiring in `FilePanel` + `Home`. Extracted crops re-enter via the unchanged `Home.HandleUpload` path.

**Tech Stack:** .NET 10 Blazor WASM (Web), .NET 8 class library (Core), `StbImageSharp` / `StbImageWriteSharp`, xUnit, bUnit, vanilla JS canvas interop.

## Global Constraints

- Core project targets **net8.0**; Web project targets **net10.0**. Do not change target frameworks.
- **No new NuGet dependencies.** PNG encoding uses `StbImageWriteSharp` (already referenced by `Mixel.Core`).
- **Core stays pure**: no Blazor, JS, or UI types in `Mixel.Core`.
- **Isolation**: do not modify existing extrusion, depth-map, export, or theming logic. Only additive changes to `FilePanel.razor`, `Home.razor`, `mixel.js`, `MixelJs.cs`, and `app.css`.
- The spritesheet is **never** added as a `FileItem`; only crops are.
- Rectangles use **inclusive** pixel bounds when drawn; minimum crop is **1×1 px**; rectangles **cannot overlap** (touching edges is allowed).
- CSS must use only the existing custom properties (`--bg`, `--panel`, `--bd`, `--acc`, `--acc-tx`, `--tx`, `--mut`, `--stage`, `--grid`).
- Every commit message ends with:
  `Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>`
- Work happens on branch `feature/spritesheet-slicer` (already created).

## File Structure

| File | Responsibility | Action |
| --- | --- | --- |
| `src/Mixel.Core/SpriteRect.cs` | Value type: rect in source-pixel space + geometry helpers | Create |
| `src/Mixel.Core/SpritesheetSlicer.cs` | Pure `Crop(RgbaImage, SpriteRect) → byte[]` PNG | Create |
| `src/Mixel.Web/Services/SliceRegion.cs` | One defined sprite: `Name` + `SpriteRect` | Create |
| `src/Mixel.Web/Services/SlicerState.cs` | JS-free logic: region list, naming, overlap, extract | Create |
| `src/Mixel.Web/wwwroot/js/mixel.js` | `initSlicer` / `renderSlicerRegions` / `disposeSlicer` | Modify (append) |
| `src/Mixel.Web/Interop/MixelJs.cs` | Wrappers for the two non-callback slicer JS calls | Modify (append) |
| `src/Mixel.Web/Components/SlicerDialog.razor` | Modal view over `SlicerState` + JS interop | Create |
| `src/Mixel.Web/wwwroot/css/app.css` | `.slicer-*` styles | Modify (append) |
| `src/Mixel.Web/Components/FilePanel.razor` | "Slice a spritesheet →" entry + callback | Modify |
| `src/Mixel.Web/Pages/Home.razor` | Open/close dialog, reuse `HandleUpload` for crops | Modify |
| `tests/Mixel.Tests/SpriteRectTests.cs` | Geometry tests | Create |
| `tests/Mixel.Tests/SpritesheetSlicerTests.cs` | Crop tests | Create |
| `tests/Mixel.Web.Tests/SlicerStateTests.cs` | Logic tests | Create |
| `tests/Mixel.Web.Tests/SlicerDialogTests.cs` | bUnit smoke tests | Create |
| `tests/Mixel.Web.Tests/FilePanelTests.cs` | Entry-point render test | Create |

---

### Task 1: `SpriteRect` value type (Core)

**Files:**
- Create: `src/Mixel.Core/SpriteRect.cs`
- Test: `tests/Mixel.Tests/SpriteRectTests.cs`

**Interfaces:**
- Produces: `public readonly record struct SpriteRect(int X, int Y, int Width, int Height)` with members `int Right`, `int Bottom`, `bool IsEmpty`, `bool Intersects(SpriteRect)`, `bool WithinBounds(int imageWidth, int imageHeight)`. Namespace `Mixel.Core`.

- [ ] **Step 1: Write the failing test**

Create `tests/Mixel.Tests/SpriteRectTests.cs`:

```csharp
using Mixel.Core;
using Xunit;

public class SpriteRectTests
{
    [Fact]
    public void Right_And_Bottom_Are_Exclusive_Edges()
    {
        var r = new SpriteRect(2, 3, 4, 5);
        Assert.Equal(6, r.Right);
        Assert.Equal(8, r.Bottom);
    }

    [Fact]
    public void IsEmpty_True_When_Zero_Or_Negative_Dimension()
    {
        Assert.True(new SpriteRect(0, 0, 0, 4).IsEmpty);
        Assert.True(new SpriteRect(0, 0, 4, 0).IsEmpty);
        Assert.False(new SpriteRect(0, 0, 1, 1).IsEmpty);
    }

    [Fact]
    public void Intersects_True_When_Overlapping()
        => Assert.True(new SpriteRect(0, 0, 4, 4).Intersects(new SpriteRect(2, 2, 4, 4)));

    [Fact]
    public void Intersects_False_When_Flush_Adjacent()
        => Assert.False(new SpriteRect(0, 0, 4, 4).Intersects(new SpriteRect(4, 0, 4, 4)));

    [Fact]
    public void Intersects_False_When_Disjoint()
        => Assert.False(new SpriteRect(0, 0, 2, 2).Intersects(new SpriteRect(8, 8, 2, 2)));

    [Fact]
    public void Intersects_True_When_Contained()
        => Assert.True(new SpriteRect(0, 0, 10, 10).Intersects(new SpriteRect(3, 3, 2, 2)));

    [Theory]
    [InlineData(0, 0, 16, 16, true)]   // exact fit
    [InlineData(8, 8, 8, 8, true)]     // edge-exact
    [InlineData(0, 0, 17, 16, false)]  // width overflow
    [InlineData(-1, 0, 4, 4, false)]   // negative origin
    [InlineData(0, 0, 0, 4, false)]    // empty width
    public void WithinBounds_Validates_Against_Image(int x, int y, int w, int h, bool expected)
        => Assert.Equal(expected, new SpriteRect(x, y, w, h).WithinBounds(16, 16));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Mixel.Tests/Mixel.Tests.csproj --filter "FullyQualifiedName~SpriteRectTests"`
Expected: FAIL — `SpriteRect` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

Create `src/Mixel.Core/SpriteRect.cs`:

```csharp
namespace Mixel.Core;

/// <summary>An axis-aligned rectangle in source-image pixel space.</summary>
public readonly record struct SpriteRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;    // exclusive
    public int Bottom => Y + Height;  // exclusive
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Edges that merely touch do not count as intersecting.</summary>
    public bool Intersects(SpriteRect o)
        => X < o.Right && Right > o.X && Y < o.Bottom && Bottom > o.Y;

    public bool WithinBounds(int imageWidth, int imageHeight)
        => X >= 0 && Y >= 0 && Width > 0 && Height > 0
           && Right <= imageWidth && Bottom <= imageHeight;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Mixel.Tests/Mixel.Tests.csproj --filter "FullyQualifiedName~SpriteRectTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Mixel.Core/SpriteRect.cs tests/Mixel.Tests/SpriteRectTests.cs
git commit -m "feat(core): add SpriteRect value type

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
```

---

### Task 2: `SpritesheetSlicer.Crop` (Core)

**Files:**
- Create: `src/Mixel.Core/SpritesheetSlicer.cs`
- Test: `tests/Mixel.Tests/SpritesheetSlicerTests.cs`

**Interfaces:**
- Consumes: `SpriteRect` (Task 1); existing `RgbaImage`, `Rgba`, `PngLoader` (`Mixel.Core`).
- Produces: `public static byte[] SpritesheetSlicer.Crop(RgbaImage source, SpriteRect rect)` — PNG bytes of the sub-region; throws `ArgumentOutOfRangeException` when `rect` is outside bounds. Namespace `Mixel.Core`.

- [ ] **Step 1: Write the failing test**

Create `tests/Mixel.Tests/SpritesheetSlicerTests.cs`:

```csharp
using System;
using Mixel.Core;
using Xunit;

public class SpritesheetSlicerTests
{
    private static readonly Rgba Solid = new(10, 20, 30, 255);

    private static RgbaImage Sheet() => TestImages.FromAscii(
        new[]
        {
            "#.#.",
            "..##",
        }, Solid);

    [Fact]
    public void Crop_Produces_Png_Of_Rect_Dimensions()
    {
        var png = SpritesheetSlicer.Crop(Sheet(), new SpriteRect(2, 0, 2, 2));
        var img = PngLoader.Load(png);
        Assert.Equal(2, img.Width);
        Assert.Equal(2, img.Height);
    }

    [Fact]
    public void Crop_RoundTrips_Pixels_From_Region()
    {
        var png = SpritesheetSlicer.Crop(Sheet(), new SpriteRect(2, 0, 2, 2));
        var img = PngLoader.Load(png);
        // region columns 2..3, rows 0..1 of the sheet:  '#' '.' / '#' '#'
        Assert.Equal(Solid, img.At(0, 0));
        Assert.Equal((byte)0, img.At(1, 0).A);
        Assert.Equal(Solid, img.At(0, 1));
        Assert.Equal(Solid, img.At(1, 1));
    }

    [Fact]
    public void Crop_OnePixel_Works()
    {
        var png = SpritesheetSlicer.Crop(Sheet(), new SpriteRect(0, 0, 1, 1));
        var img = PngLoader.Load(png);
        Assert.Equal(1, img.Width);
        Assert.Equal(1, img.Height);
        Assert.Equal(Solid, img.At(0, 0));
    }

    [Fact]
    public void Crop_FullImage_Works()
    {
        var png = SpritesheetSlicer.Crop(Sheet(), new SpriteRect(0, 0, 4, 2));
        var img = PngLoader.Load(png);
        Assert.Equal(4, img.Width);
        Assert.Equal(2, img.Height);
    }

    [Fact]
    public void Crop_OutOfBounds_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => SpritesheetSlicer.Crop(Sheet(), new SpriteRect(3, 0, 2, 2)));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Mixel.Tests/Mixel.Tests.csproj --filter "FullyQualifiedName~SpritesheetSlicerTests"`
Expected: FAIL — `SpritesheetSlicer` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Mixel.Core/SpritesheetSlicer.cs`:

```csharp
using StbImageWriteSharp;

namespace Mixel.Core;

/// <summary>Crops named sub-regions out of a spritesheet and encodes them as PNGs.</summary>
public static class SpritesheetSlicer
{
    /// <summary>Crops <paramref name="rect"/> out of <paramref name="source"/> and
    /// encodes the result as a PNG. Pure; no UI dependency.</summary>
    public static byte[] Crop(RgbaImage source, SpriteRect rect)
    {
        if (!rect.WithinBounds(source.Width, source.Height))
            throw new ArgumentOutOfRangeException(nameof(rect),
                $"rect {rect} is outside image bounds {source.Width}x{source.Height}");

        int w = rect.Width, h = rect.Height;
        var data = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = source.At(rect.X + x, rect.Y + y);
                int b = (y * w + x) * 4;
                data[b] = p.R; data[b + 1] = p.G; data[b + 2] = p.B; data[b + 3] = p.A;
            }

        using var ms = new MemoryStream();
        new ImageWriter().WritePng(data, w, h, ColorComponents.RedGreenBlueAlpha, ms);
        return ms.ToArray();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Mixel.Tests/Mixel.Tests.csproj --filter "FullyQualifiedName~SpritesheetSlicerTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Mixel.Core/SpritesheetSlicer.cs tests/Mixel.Tests/SpritesheetSlicerTests.cs
git commit -m "feat(core): add SpritesheetSlicer.Crop

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
```

---

### Task 3: `SliceRegion` + `SlicerState` (Web logic)

**Files:**
- Create: `src/Mixel.Web/Services/SliceRegion.cs`
- Create: `src/Mixel.Web/Services/SlicerState.cs`
- Test: `tests/Mixel.Web.Tests/SlicerStateTests.cs`

**Interfaces:**
- Consumes: `SpriteRect`, `SpritesheetSlicer`, `RgbaImage`, `ImageSize` (`Mixel.Core`).
- Produces (namespace `Mixel.Web.Services`):
  - `public sealed class SliceRegion { public required string Name { get; set; } public required SpriteRect Rect { get; init; } }`
  - `public sealed class SlicerState` with:
    - `IReadOnlyList<SliceRegion> Regions { get; }`
    - `bool TryAdd(SpriteRect rect, out string? error)`
    - `void Remove(SliceRegion region)`
    - `void Rename(SliceRegion region, string name)`
    - `string NextName(SliceRegion? excluding = null)`
    - `bool IsStandardSize(SliceRegion region)`
    - `List<(string name, byte[] bytes)> BuildExtract(RgbaImage sheet)`

- [ ] **Step 1: Write the failing test**

Create `tests/Mixel.Web.Tests/SlicerStateTests.cs`:

```csharp
using System.Linq;
using Mixel.Core;
using Mixel.Web.Services;
using Xunit;

public class SlicerStateTests
{
    private static RgbaImage Sheet(int w = 4, int h = 2)
    {
        var px = new Rgba[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = new Rgba(9, 9, 9, 255);
        return new RgbaImage { Width = w, Height = h, Pixels = px };
    }

    [Fact]
    public void TryAdd_AutoNames_Sequentially()
    {
        var s = new SlicerState();
        Assert.True(s.TryAdd(new SpriteRect(0, 0, 1, 1), out _));
        Assert.True(s.TryAdd(new SpriteRect(2, 0, 1, 1), out _));
        Assert.Equal(new[] { "sprite_01", "sprite_02" }, s.Regions.Select(r => r.Name));
    }

    [Fact]
    public void TryAdd_Reuses_Lowest_Free_Name_After_Remove()
    {
        var s = new SlicerState();
        s.TryAdd(new SpriteRect(0, 0, 1, 1), out _); // sprite_01
        s.TryAdd(new SpriteRect(1, 0, 1, 1), out _); // sprite_02
        s.TryAdd(new SpriteRect(2, 0, 1, 1), out _); // sprite_03
        s.Remove(s.Regions.First(r => r.Name == "sprite_02"));
        s.TryAdd(new SpriteRect(3, 0, 1, 1), out _);
        Assert.Contains(s.Regions, r => r.Name == "sprite_02");
        Assert.Equal(3, s.Regions.Count);
    }

    [Fact]
    public void TryAdd_Rejects_Overlap()
    {
        var s = new SlicerState();
        s.TryAdd(new SpriteRect(0, 0, 2, 2), out _);
        var ok = s.TryAdd(new SpriteRect(1, 1, 2, 2), out var error);
        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(error));
        Assert.Single(s.Regions);
    }

    [Fact]
    public void TryAdd_Allows_Flush_Adjacent()
    {
        var s = new SlicerState();
        s.TryAdd(new SpriteRect(0, 0, 2, 2), out _);
        Assert.True(s.TryAdd(new SpriteRect(2, 0, 2, 2), out _));
    }

    [Fact]
    public void TryAdd_Rejects_Empty()
    {
        var s = new SlicerState();
        Assert.False(s.TryAdd(new SpriteRect(0, 0, 0, 0), out _));
        Assert.Empty(s.Regions);
    }

    [Fact]
    public void Rename_Trims_And_Falls_Back_When_Blank()
    {
        var s = new SlicerState();
        s.TryAdd(new SpriteRect(0, 0, 1, 1), out _);
        var r = s.Regions[0];
        s.Rename(r, "  hero  ");
        Assert.Equal("hero", r.Name);
        s.Rename(r, "   ");
        Assert.StartsWith("sprite_", r.Name);
    }

    [Fact]
    public void IsStandardSize_Detects_PowerOfTwo()
    {
        var s = new SlicerState();
        s.TryAdd(new SpriteRect(0, 0, 16, 16), out _);
        s.TryAdd(new SpriteRect(20, 0, 3, 3), out _);
        Assert.True(s.IsStandardSize(s.Regions[0]));
        Assert.False(s.IsStandardSize(s.Regions[1]));
    }

    [Fact]
    public void BuildExtract_Produces_One_Png_Per_Region_With_Png_Names()
    {
        var s = new SlicerState();
        s.TryAdd(new SpriteRect(0, 0, 2, 2), out _);
        s.TryAdd(new SpriteRect(2, 0, 2, 2), out _);
        var outFiles = s.BuildExtract(Sheet());
        Assert.Equal(2, outFiles.Count);
        Assert.Equal("sprite_01.png", outFiles[0].name);
        var img = PngLoader.Load(outFiles[0].bytes);
        Assert.Equal(2, img.Width);
        Assert.Equal(2, img.Height);
    }

    [Fact]
    public void BuildExtract_Dedupes_Duplicate_Names()
    {
        var s = new SlicerState();
        s.TryAdd(new SpriteRect(0, 0, 2, 2), out _);
        s.TryAdd(new SpriteRect(2, 0, 2, 2), out _);
        s.Rename(s.Regions[0], "hero");
        s.Rename(s.Regions[1], "hero");
        var names = s.BuildExtract(Sheet()).Select(f => f.name).ToArray();
        Assert.Equal(new[] { "hero.png", "hero_2.png" }, names);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Mixel.Web.Tests/Mixel.Web.Tests.csproj --filter "FullyQualifiedName~SlicerStateTests"`
Expected: FAIL — `SliceRegion` / `SlicerState` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/Mixel.Web/Services/SliceRegion.cs`:

```csharp
using Mixel.Core;

namespace Mixel.Web.Services;

public sealed class SliceRegion
{
    public required string Name { get; set; }   // no extension; export appends ".png"
    public required SpriteRect Rect { get; init; }
}
```

Create `src/Mixel.Web/Services/SlicerState.cs`:

```csharp
using Mixel.Core;

namespace Mixel.Web.Services;

/// <summary>JS-free logic behind the spritesheet slicer: the region list,
/// auto-naming, overlap rules, and crop extraction. Mirrors DepthPainterState
/// so the feature's behaviour is unit-testable without a browser.</summary>
public sealed class SlicerState
{
    private readonly List<SliceRegion> _regions = new();
    public IReadOnlyList<SliceRegion> Regions => _regions;

    /// <summary>Adds a region unless it is empty or overlaps an existing one.</summary>
    public bool TryAdd(SpriteRect rect, out string? error)
    {
        if (rect.IsEmpty)
        {
            error = "Selection is too small.";
            return false;
        }
        foreach (var r in _regions)
            if (r.Rect.Intersects(rect))
            {
                error = "Sprites can't overlap.";
                return false;
            }
        _regions.Add(new SliceRegion { Name = NextName(), Rect = rect });
        error = null;
        return true;
    }

    public void Remove(SliceRegion region) => _regions.Remove(region);

    public void Rename(SliceRegion region, string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        region.Name = trimmed.Length == 0 ? NextName(region) : trimmed;
    }

    /// <summary>Lowest unused "sprite_NN" name, ignoring <paramref name="excluding"/>.</summary>
    public string NextName(SliceRegion? excluding = null)
    {
        for (int n = 1; ; n++)
        {
            var candidate = $"sprite_{n:D2}";
            bool taken = false;
            foreach (var r in _regions)
            {
                if (ReferenceEquals(r, excluding)) continue;
                if (r.Name == candidate) { taken = true; break; }
            }
            if (!taken) return candidate;
        }
    }

    public bool IsStandardSize(SliceRegion region)
        => ImageSize.IsStandard(region.Rect.Width, region.Rect.Height);

    /// <summary>Crops every region from <paramref name="sheet"/>, de-duplicating
    /// output names within the batch ("hero.png", "hero_2.png", …).</summary>
    public List<(string name, byte[] bytes)> BuildExtract(RgbaImage sheet)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string, byte[])>(_regions.Count);
        foreach (var region in _regions)
        {
            var name = region.Name;
            int suffix = 2;
            while (!used.Add(name))
                name = $"{region.Name}_{suffix++}";
            var png = SpritesheetSlicer.Crop(sheet, region.Rect);
            result.Add(($"{name}.png", png));
        }
        return result;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Mixel.Web.Tests/Mixel.Web.Tests.csproj --filter "FullyQualifiedName~SlicerStateTests"`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Mixel.Web/Services/SliceRegion.cs src/Mixel.Web/Services/SlicerState.cs tests/Mixel.Web.Tests/SlicerStateTests.cs
git commit -m "feat(web): add SlicerState slicing logic

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
```

---

### Task 4: Slicer JS interop (mixel.js + MixelJs wrappers)

**Files:**
- Modify: `src/Mixel.Web/wwwroot/js/mixel.js` (append functions before the final `};`)
- Modify: `src/Mixel.Web/Interop/MixelJs.cs` (append two wrappers)

**Interfaces:**
- Produces (JS, on `window.mixel`):
  - `initSlicer(baseId, overlayId, rgbaBytes, w, h, dotNetRef)` — draws the sheet, wires rubber-band pointer input; on pointer-up invokes `dotNetRef.invokeMethodAsync('OnRegionDrawn', x, y, w, h)` with inclusive natural-pixel bounds clamped to the image.
  - `renderSlicerRegions(overlayId, regions)` — `regions` is an array of `{ x, y, w, h, name }`; redraws all committed outlines + labels.
  - `disposeSlicer(overlayId)` — removes pointer listeners and cached state.
- Produces (C#, `MixelJs`): `ValueTask RenderSlicerRegionsAsync(string overlayId, object regions)`, `ValueTask DisposeSlicerAsync(string overlayId)`.
- Note: `initSlicer` is invoked directly via `IJSRuntime` from the component (it needs the `DotNetObjectReference`), mirroring how `DepthPainter` calls `mixel.listenCanvasInput`. It is **not** wrapped in `MixelJs` to keep `Interop` free of a dependency on `Components`.

- [ ] **Step 1: Append the JS functions**

In `src/Mixel.Web/wwwroot/js/mixel.js`, immediately before the closing `};` of the `window.mixel = { … }` object (after the `fetchBytes` entry), add:

```javascript
  // ---- spritesheet slicer ----
  _paintSlicer: function (overlay, liveRect) {
    if (!overlay || !overlay.__mixelNat) return;
    const { w, h } = overlay.__mixelNat;
    const ctx = overlay.getContext("2d");
    ctx.clearRect(0, 0, overlay.width, overlay.height);
    const sX = overlay.width / w, sY = overlay.height / h;
    ctx.font = "bold 12px monospace";
    ctx.textBaseline = "top";
    const one = (rg, withLabel) => {
      ctx.lineWidth = 2;
      ctx.strokeStyle = "rgba(255,255,255,0.95)";
      ctx.strokeRect(rg.x * sX + 1, rg.y * sY + 1, rg.w * sX - 2, rg.h * sY - 2);
      if (withLabel && rg.name != null) {
        const tw = ctx.measureText(rg.name).width + 8;
        ctx.fillStyle = "rgba(0,0,0,0.75)";
        ctx.fillRect(rg.x * sX + 1, rg.y * sY + 1, tw, 16);
        ctx.fillStyle = "#ffffff";
        ctx.fillText(rg.name, rg.x * sX + 5, rg.y * sY + 3);
      }
    };
    for (const rg of (overlay.__mixelRegions || [])) one(rg, true);
    if (liveRect) one(liveRect, false);
  },

  initSlicer: function (baseId, overlayId, rgbaBytes, w, h, dotNetRef) {
    const base = document.getElementById(baseId);
    const overlay = document.getElementById(overlayId);
    if (!base || !overlay) return;

    base.width = w; base.height = h;
    const wrap = base.closest(".slicer-canvas-wrap") || base.parentElement;
    let scale = 1;
    if (wrap) {
      const availW = wrap.clientWidth - 32;
      const availH = wrap.clientHeight - 32;
      scale = Math.max(1, Math.min(Math.floor(availW / w), Math.floor(availH / h)));
    }
    const dispW = w * scale, dispH = h * scale;
    base.style.width = dispW + "px";
    base.style.height = dispH + "px";

    const bctx = base.getContext("2d");
    const imageData = bctx.createImageData(w, h);
    for (let i = 0; i < rgbaBytes.length; i++) imageData.data[i] = rgbaBytes[i];
    bctx.putImageData(imageData, 0, 0);

    overlay.width = dispW; overlay.height = dispH;
    overlay.style.width = dispW + "px";
    overlay.style.height = dispH + "px";
    overlay.__mixelNat = { w, h };
    overlay.__mixelRegions = [];

    const toPixel = (e) => {
      const r = overlay.getBoundingClientRect();
      let x = Math.floor((e.clientX - r.left) / r.width  * w);
      let y = Math.floor((e.clientY - r.top)  / r.height * h);
      x = Math.max(0, Math.min(w - 1, x));
      y = Math.max(0, Math.min(h - 1, y));
      return { x, y };
    };

    let dragging = false, sx = 0, sy = 0;
    const norm = (px, py) => ({
      x: Math.min(sx, px), y: Math.min(sy, py),
      w: Math.abs(px - sx) + 1, h: Math.abs(py - sy) + 1,
    });

    const onDown = (e) => {
      dragging = true;
      overlay.setPointerCapture(e.pointerId);
      const p = toPixel(e); sx = p.x; sy = p.y;
    };
    const onMove = (e) => {
      if (!dragging) return;
      const p = toPixel(e);
      window.mixel._paintSlicer(overlay, norm(p.x, p.y));
    };
    const onUp = (e) => {
      if (!dragging) return;
      dragging = false;
      const p = toPixel(e);
      const r = norm(p.x, p.y);
      if (r.x + r.w > w) r.w = w - r.x;
      if (r.y + r.h > h) r.h = h - r.y;
      window.mixel._paintSlicer(overlay, null);
      dotNetRef.invokeMethodAsync("OnRegionDrawn", r.x, r.y, r.w, r.h);
    };

    overlay.addEventListener("pointerdown", onDown);
    overlay.addEventListener("pointermove", onMove);
    overlay.addEventListener("pointerup", onUp);
    overlay.__mixelSlicerCleanup = () => {
      overlay.removeEventListener("pointerdown", onDown);
      overlay.removeEventListener("pointermove", onMove);
      overlay.removeEventListener("pointerup", onUp);
    };
  },

  renderSlicerRegions: function (overlayId, regions) {
    const overlay = document.getElementById(overlayId);
    if (!overlay) return;
    overlay.__mixelRegions = regions || [];
    window.mixel._paintSlicer(overlay, null);
  },

  disposeSlicer: function (overlayId) {
    const overlay = document.getElementById(overlayId);
    if (!overlay) return;
    if (overlay.__mixelSlicerCleanup) { overlay.__mixelSlicerCleanup(); overlay.__mixelSlicerCleanup = null; }
    overlay.__mixelRegions = null;
    overlay.__mixelNat = null;
  },
```

(Ensure the entry immediately above your additions ends with a trailing comma, matching the existing `fetchBytes` block.)

- [ ] **Step 2: Append the C# wrappers**

In `src/Mixel.Web/Interop/MixelJs.cs`, add before the closing brace of the class:

```csharp
    public ValueTask RenderSlicerRegionsAsync(string overlayId, object regions)
        => _js.InvokeVoidAsync("mixel.renderSlicerRegions", overlayId, regions);

    public ValueTask DisposeSlicerAsync(string overlayId)
        => _js.InvokeVoidAsync("mixel.disposeSlicer", overlayId);
```

- [ ] **Step 3: Verify the project builds**

Run: `dotnet build src/Mixel.Web/Mixel.Web.csproj`
Expected: Build succeeded, 0 errors. (JS has no automated test here; it is exercised end-to-end after Task 5.)

- [ ] **Step 4: Commit**

```bash
git add src/Mixel.Web/wwwroot/js/mixel.js src/Mixel.Web/Interop/MixelJs.cs
git commit -m "feat(web): add slicer canvas JS interop

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
```

---

### Task 5: `SlicerDialog.razor` modal + styles

**Files:**
- Create: `src/Mixel.Web/Components/SlicerDialog.razor`
- Modify: `src/Mixel.Web/wwwroot/css/app.css` (append `.slicer-*` rules)
- Test: `tests/Mixel.Web.Tests/SlicerDialogTests.cs`

**Interfaces:**
- Consumes: `SlicerState`, `SliceRegion` (Task 3); `PngLoader`, `RgbaImage`, `InvalidPngException`, `SpriteRect` (Core); `MixelJs.RenderSlicerRegionsAsync` / `DisposeSlicerAsync` (Task 4); `IJSRuntime` for `mixel.initSlicer`.
- Produces: component `Mixel.Web.Components.SlicerDialog` with parameters `byte[] SheetBytes`, `string SheetName`, `bool AllowNonStandardSize`, `EventCallback<IReadOnlyList<(string name, byte[] bytes)>> OnExtract`, `EventCallback OnClose`, and `[JSInvokable] Task OnRegionDrawn(int x, int y, int w, int h)`.

- [ ] **Step 1: Write the failing test**

Create `tests/Mixel.Web.Tests/SlicerDialogTests.cs`:

```csharp
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Mixel.Web.Components;
using Mixel.Web.Interop;
using Xunit;

public class SlicerDialogTests : BunitContext
{
    private void Setup()
    {
        JSInterop.Mode = JSRuntimeMode.Loose; // tolerate initSlicer interop during render
        Services.AddScoped<MixelJs>();
    }

    [Fact]
    public void Renders_Header_And_Disabled_Confirm_For_Valid_Sheet()
    {
        Setup();
        var png = PngFixture.Solid(16, 16, 200, 100, 50);
        var cut = Render<SlicerDialog>(p => p
            .Add(x => x.SheetBytes, png)
            .Add(x => x.SheetName, "sheet.png"));

        Assert.NotNull(cut.Find("[data-test=slicer-overlay]"));
        var confirm = cut.Find("[data-test=slicer-confirm]");
        Assert.True(confirm.HasAttribute("disabled"));
    }

    [Fact]
    public void Renders_Error_State_For_Invalid_Png()
    {
        Setup();
        var cut = Render<SlicerDialog>(p => p
            .Add(x => x.SheetBytes, new byte[] { 1, 2, 3, 4 })
            .Add(x => x.SheetName, "broken.png"));

        Assert.NotNull(cut.Find("[data-test=slicer-error]"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Mixel.Web.Tests/Mixel.Web.Tests.csproj --filter "FullyQualifiedName~SlicerDialogTests"`
Expected: FAIL — `SlicerDialog` does not exist.

- [ ] **Step 3: Create the component**

Create `src/Mixel.Web/Components/SlicerDialog.razor`:

```razor
@using Mixel.Core
@using Mixel.Web.Interop
@using Mixel.Web.Services
@using Microsoft.JSInterop
@inject MixelJs Js
@inject IJSRuntime JSRuntime
@implements IAsyncDisposable

<div class="slicer-overlay" data-test="slicer-overlay">
    <div class="slicer-panel">
        <div class="slicer-head">
            <span class="slicer-title">Slice spritesheet — @SheetName</span>
            <button type="button" class="icon-btn" aria-label="Close slicer" @onclick="CloseAsync">✕</button>
        </div>

        @if (_error is not null)
        {
            <div class="slicer-error" data-test="slicer-error">@_error</div>
        }
        else
        {
            <div class="slicer-body">
                <div class="slicer-canvas-wrap">
                    <div class="slicer-stack">
                        <canvas id="slicer-canvas" class="slicer-canvas"></canvas>
                        <canvas id="slicer-overlay-canvas" class="slicer-overlay-canvas"></canvas>
                    </div>
                </div>

                <div class="slicer-side">
                    <p class="slicer-hint">Drag on the sheet to mark a sprite.</p>
                    @if (_notice is not null)
                    {
                        <div class="slicer-notice" data-test="slicer-notice">@_notice</div>
                    }
                    <ul class="slicer-regions" data-test="slicer-regions">
                        @foreach (var region in _state.Regions)
                        {
                            var r = region;
                            <li class="slicer-region">
                                <input class="slicer-name" value="@r.Name"
                                       @onchange="e => RenameAsync(r, e.Value?.ToString())" />
                                <span class="slicer-size @SizeClass(r)">@r.Rect.Width×@r.Rect.Height</span>
                                <button type="button" class="icon-btn" aria-label="Delete sprite"
                                        @onclick="() => RemoveAsync(r)">✕</button>
                            </li>
                        }
                    </ul>
                    <button type="button" class="btn primary slicer-confirm" data-test="slicer-confirm"
                            disabled="@(_state.Regions.Count == 0)" @onclick="ConfirmAsync">
                        Add @_state.Regions.Count sprite@(_state.Regions.Count == 1 ? "" : "s") to editor
                    </button>
                </div>
            </div>
        }
    </div>
</div>

@code {
    [Parameter] public byte[] SheetBytes { get; set; } = default!;
    [Parameter] public string SheetName  { get; set; } = "spritesheet.png";
    [Parameter] public bool AllowNonStandardSize { get; set; }
    [Parameter] public EventCallback<IReadOnlyList<(string name, byte[] bytes)>> OnExtract { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private readonly SlicerState _state = new();
    private RgbaImage? _image;
    private string? _error;
    private string? _notice;
    private DotNetObjectReference<SlicerDialog>? _self;
    private bool _canvasReady;

    protected override void OnInitialized()
    {
        try { _image = PngLoader.Load(SheetBytes); }
        catch (InvalidPngException ex) { _error = $"Could not open spritesheet: {ex.Message}"; }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_image is null || _canvasReady) return;
        _canvasReady = true;
        _self = DotNetObjectReference.Create(this);
        await JSRuntime.InvokeVoidAsync("mixel.initSlicer", "slicer-canvas", "slicer-overlay-canvas",
            ToRgbaBytes(_image), _image.Width, _image.Height, _self);
    }

    [JSInvokable]
    public async Task OnRegionDrawn(int x, int y, int w, int h)
    {
        if (_image is null) return;
        if (_state.TryAdd(new SpriteRect(x, y, w, h), out var error))
        {
            _notice = null;
            await RenderRegionsAsync();
        }
        else
        {
            _notice = error;
        }
        StateHasChanged();
    }

    private async Task RenderRegionsAsync()
    {
        var payload = _state.Regions
            .Select(r => new { x = r.Rect.X, y = r.Rect.Y, w = r.Rect.Width, h = r.Rect.Height, name = r.Name })
            .ToArray();
        await Js.RenderSlicerRegionsAsync("slicer-overlay-canvas", payload);
    }

    private async Task RenameAsync(SliceRegion region, string? name)
    {
        _state.Rename(region, name ?? string.Empty);
        await RenderRegionsAsync();
    }

    private async Task RemoveAsync(SliceRegion region)
    {
        _state.Remove(region);
        await RenderRegionsAsync();
    }

    private string SizeClass(SliceRegion region)
        => (!AllowNonStandardSize && !_state.IsStandardSize(region)) ? "nonstandard" : "";

    private async Task ConfirmAsync()
    {
        if (_image is null || _state.Regions.Count == 0) return;
        await OnExtract.InvokeAsync(_state.BuildExtract(_image));
    }

    private async Task CloseAsync() => await OnClose.InvokeAsync();

    private static byte[] ToRgbaBytes(RgbaImage img)
    {
        var data = new byte[img.Pixels.Length * 4];
        for (int i = 0; i < img.Pixels.Length; i++)
        {
            var p = img.Pixels[i];
            data[i * 4] = p.R; data[i * 4 + 1] = p.G; data[i * 4 + 2] = p.B; data[i * 4 + 3] = p.A;
        }
        return data;
    }

    public async ValueTask DisposeAsync()
    {
        try { await Js.DisposeSlicerAsync("slicer-overlay-canvas"); } catch { }
        _self?.Dispose();
    }
}
```

- [ ] **Step 4: Append the styles**

Append to `src/Mixel.Web/wwwroot/css/app.css`:

```css
/* ---- spritesheet slicer ---- */
.slicer-overlay { position: fixed; inset: 0; z-index: 50; display: flex; align-items: center; justify-content: center;
    background: rgba(0,0,0,.55); }
.slicer-panel { display: flex; flex-direction: column; width: min(1100px, 94vw); height: min(760px, 92vh);
    background: var(--panel); border: 1px solid var(--bd); border-radius: 14px; overflow: hidden;
    box-shadow: 0 18px 60px rgba(0,0,0,.5); }
.slicer-head { display: flex; align-items: center; justify-content: space-between; padding: 14px 16px;
    border-bottom: 1px solid var(--bd); }
.slicer-title { font: 700 13px/1 var(--font-head); letter-spacing: .04em; }
.slicer-error { padding: 24px; color: var(--tx); font-size: 14px; }
.slicer-body { flex: 1; display: flex; min-height: 0; }
.slicer-canvas-wrap { flex: 1; display: flex; align-items: center; justify-content: center;
    overflow: auto; background: var(--stage); padding: 16px; min-width: 0; }
.slicer-stack { position: relative; display: inline-flex; flex: 0 0 auto; }
.slicer-canvas { image-rendering: pixelated; image-rendering: crisp-edges; display: block; }
.slicer-overlay-canvas { position: absolute; inset: 0; cursor: crosshair; }
.slicer-side { flex: 0 0 280px; display: flex; flex-direction: column; gap: 10px; padding: 16px;
    border-left: 1px solid var(--bd); overflow: auto; }
.slicer-hint { color: var(--mut); font-size: 13px; margin: 0; }
.slicer-notice { background: var(--acc); color: var(--acc-tx); border-radius: 8px; padding: 8px 10px;
    font: 600 12px/1.3 var(--font-body); }
.slicer-regions { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 6px; flex: 1; }
.slicer-region { display: flex; align-items: center; gap: 8px; }
.slicer-name { flex: 1; min-width: 0; background: var(--bg); color: var(--tx); border: 1px solid var(--bd);
    border-radius: 7px; padding: 6px 8px; font: 500 13px/1 var(--font-body); }
.slicer-size { font: 600 11px/1 var(--font-head); color: var(--mut); font-variant-numeric: tabular-nums; }
.slicer-size.nonstandard { color: var(--acc); }
.slicer-confirm { width: 100%; }
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/Mixel.Web.Tests/Mixel.Web.Tests.csproj --filter "FullyQualifiedName~SlicerDialogTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Mixel.Web/Components/SlicerDialog.razor src/Mixel.Web/wwwroot/css/app.css tests/Mixel.Web.Tests/SlicerDialogTests.cs
git commit -m "feat(web): add SlicerDialog modal and styles

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
```

---

### Task 6: Entry point + Home wiring

**Files:**
- Modify: `src/Mixel.Web/Components/FilePanel.razor`
- Modify: `src/Mixel.Web/Pages/Home.razor`
- Test: `tests/Mixel.Web.Tests/FilePanelTests.cs`

**Interfaces:**
- Consumes: `SlicerDialog` (Task 5); existing `Home.HandleUpload(IReadOnlyList<(string name, byte[] bytes)>)`.
- Produces: `FilePanel` parameter `EventCallback<(string name, byte[] bytes)> OnImportSpritesheet` and a "Slice a spritesheet →" control; `Home` fields `_sheetBytes` / `_sheetName` plus methods `OpenSlicer`, `CloseSlicer`, `OnSlicerExtract`.

- [ ] **Step 1: Write the failing test**

Create `tests/Mixel.Web.Tests/FilePanelTests.cs`:

```csharp
using System.Collections.Generic;
using Bunit;
using Mixel.Web.Components;
using Mixel.Web.Services;
using Xunit;

public class FilePanelTests : BunitContext
{
    [Fact]
    public void Renders_Spritesheet_Slicer_Entry()
    {
        var cut = Render<FilePanel>(p => p
            .Add(x => x.Items, new List<FileItem>()));

        Assert.Contains("Slice a spritesheet", cut.Markup);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Mixel.Web.Tests/Mixel.Web.Tests.csproj --filter "FullyQualifiedName~FilePanelTests"`
Expected: FAIL — markup does not contain "Slice a spritesheet".

- [ ] **Step 3: Add the FilePanel entry point**

In `src/Mixel.Web/Components/FilePanel.razor`, directly after the existing `sample-btn` button line:

```razor
        <button type="button" class="link-btn sample-btn" @onclick="OnLoadSample">Try sample →</button>
```

add:

```razor
        <label class="link-btn slicer-link">
            <InputFile OnChange="OnSheetInput" accept=".png" />
            <span>Slice a spritesheet →</span>
        </label>
```

In the `@code` block, add the parameter and handler (alongside the existing members):

```csharp
    [Parameter] public EventCallback<(string name, byte[] bytes)> OnImportSpritesheet { get; set; }

    private async Task OnSheetInput(InputFileChangeEventArgs e)
    {
        var file = e.File;
        using var ms = new MemoryStream();
        await using var stream = file.OpenReadStream(maxAllowedSize: 64 * 1024 * 1024);
        await stream.CopyToAsync(ms);
        await OnImportSpritesheet.InvokeAsync((file.Name, ms.ToArray()));
    }
```

Append to `src/Mixel.Web/wwwroot/css/app.css`:

```css
.slicer-link { display: inline-block; position: relative; margin-top: 8px; cursor: pointer; font-size: 12px; }
.slicer-link input { position: absolute; inset: 0; opacity: 0; cursor: pointer; }
```

- [ ] **Step 4: Run the FilePanel test to verify it passes**

Run: `dotnet test tests/Mixel.Web.Tests/Mixel.Web.Tests.csproj --filter "FullyQualifiedName~FilePanelTests"`
Expected: PASS (1 test).

- [ ] **Step 5: Wire the dialog into Home**

In `src/Mixel.Web/Pages/Home.razor`, update the `<FilePanel ... />` opening tag to pass the new callback (add the attribute to the existing element):

```razor
    <FilePanel Items="_files" SelectedPreview="_selected"
               OnUpload="HandleUpload" OnLoadSample="LoadSampleAsync"
               OnPreview="SelectAsync" OnToggle="ToggleAsync"
               OnSelectAll="SelectAll" OnDeselectAll="DeselectAll"
               OnImportSpritesheet="OpenSlicer" />
```

Immediately after the closing `</div>` of `<div class="workspace">`, add the conditional dialog:

```razor
@if (_sheetBytes is not null)
{
    <SlicerDialog SheetBytes="_sheetBytes" SheetName="_sheetName"
                  AllowNonStandardSize="_settings.AllowNonStandardSize"
                  OnExtract="OnSlicerExtract" OnClose="CloseSlicer" />
}
```

In the `@code` block, add fields next to the existing private fields:

```csharp
    private byte[]? _sheetBytes;
    private string  _sheetName = "spritesheet.png";
```

and these methods (anywhere in `@code`):

```csharp
    private void OpenSlicer((string name, byte[] bytes) sheet)
    {
        _sheetName  = sheet.name;
        _sheetBytes = sheet.bytes;
        StateHasChanged();
    }

    private void CloseSlicer() { _sheetBytes = null; StateHasChanged(); }

    private async Task OnSlicerExtract(IReadOnlyList<(string name, byte[] bytes)> sprites)
    {
        _sheetBytes = null;          // close the dialog
        await HandleUpload(sprites); // existing path: add FileItems, select first, regen
    }
```

- [ ] **Step 6: Run the full Web test suite to verify no regressions**

Run: `dotnet test tests/Mixel.Web.Tests/Mixel.Web.Tests.csproj`
Expected: PASS — all tests green, including existing `HomeRenderTests`.

- [ ] **Step 7: Commit**

```bash
git add src/Mixel.Web/Components/FilePanel.razor src/Mixel.Web/Pages/Home.razor src/Mixel.Web/wwwroot/css/app.css tests/Mixel.Web.Tests/FilePanelTests.cs
git commit -m "feat(web): wire spritesheet slicer into FilePanel and Home

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
```

---

### Task 7: Full verification

**Files:** none (verification only)

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build`
Expected: Build succeeded, 0 errors, 0 new warnings.

- [ ] **Step 2: Run all Core tests**

Run: `dotnet test tests/Mixel.Tests/Mixel.Tests.csproj`
Expected: PASS — existing suites plus `SpriteRectTests` (8) and `SpritesheetSlicerTests` (5).

- [ ] **Step 3: Run all Web tests**

Run: `dotnet test tests/Mixel.Web.Tests/Mixel.Web.Tests.csproj`
Expected: PASS — existing suites plus `SlicerStateTests` (9), `SlicerDialogTests` (2), `FilePanelTests` (1).

- [ ] **Step 4: Manual smoke test (browser)**

Run: `dotnet run --project src/Mixel.Web/Mixel.Web.csproj`, open the app, and confirm:
  1. "Slice a spritesheet →" appears under the upload control.
  2. Selecting a PNG opens the modal showing the sheet.
  3. Dragging draws a rectangle; releasing adds a named region (`sprite_01`).
  4. Overlapping drag is rejected with the "Sprites can't overlap" notice.
  5. Renaming and deleting regions updates the overlay labels.
  6. "Add N sprites to editor" closes the modal and the crops appear in the file list, each selectable/preview-able exactly like an uploaded PNG.
  7. The spritesheet itself is **not** in the file list.

No commit (verification only).

---

## Self-Review

**1. Spec coverage**

| Spec section | Task(s) |
| --- | --- |
| §3 Core `SpriteRect` | Task 1 |
| §4.2 `SpritesheetSlicer.Crop` | Task 2 |
| §5.1 `SliceRegion`, §5.2 `SlicerState` | Task 3 |
| §5.4 JS interop + MixelJs wrappers | Task 4 |
| §5.3 `SlicerDialog.razor`, §5 styles | Task 5 |
| §5.5 FilePanel entry, §5.6 Home wiring | Task 6 |
| §7 Error handling | Task 3 (overlap/empty), Task 5 (invalid PNG), Task 2 (bounds) |
| §8 Testing strategy | Tasks 1–6 tests + Task 7 |
| §9 Isolation guarantees | Additive-only changes throughout; Task 6 Step 6 regression run |

No spec requirement is left without a task.

**2. Placeholder scan:** No "TBD"/"TODO"/"handle edge cases"/"similar to" placeholders; every code step contains complete code.

**3. Type consistency:** `SpriteRect(X,Y,Width,Height)` and its members (`Right`, `Bottom`, `IsEmpty`, `Intersects`, `WithinBounds`) are used identically in Tasks 1–5. `SlicerState` members (`TryAdd`, `Remove`, `Rename`, `NextName`, `IsStandardSize`, `BuildExtract`) match between Task 3's definition and Task 5's usage. `OnRegionDrawn(int,int,int,int)`, `RenderSlicerRegionsAsync(string, object)`, and `DisposeSlicerAsync(string)` signatures are consistent across Tasks 4 and 5. `OnImportSpritesheet` / `OnExtract` / `OnClose` callback types match between FilePanel/SlicerDialog (producers) and Home (consumer).
