# Per-Pixel Depth Extrusion — Design Spec

**Date:** 2026-06-29
**Scope:** `Mixel.Core` + `Mixel.Web` only. No CLI changes.

## Goal

KenneyShape-style per-pixel depth extrusion. Users paint discrete depth levels onto
pixel art in-browser; the core pipeline produces a glTF mesh where each pixel's front
face sits at its painted Z level, creating a stairstepped silhouette.

A **Simple / Per-pixel mode toggle** preserves the existing uniform-depth workflow.

---

## 1. Core (`Mixel.Core`)

### 1.1 New type: `DepthMap`

```
Mixel.Core/DepthMap.cs  (NEW)
```

```csharp
public sealed class DepthMap
{
    public required int Width  { get; init; }
    public required int Height { get; init; }
    /// <summary>Row-major. 0 = air, 1..N = depth level.</summary>
    public required byte[] Levels { get; init; }
    public required int OffsetX { get; init; }   // bbox origin in source image
    public required int OffsetY { get; init; }

    public byte At(int x, int y)
        => x >= 0 && y >= 0 && x < Width && y < Height
            ? Levels[y * Width + x] : (byte)0;

    public int MaxLevel => Levels.Length == 0 ? 0 : Levels.Max();
}
```

Convention mirrors the existing `Mask` bbox-crop so interop between the two is trivial.

### 1.2 `ExtrudeOptions` — add `DepthMap?`

```
Mixel.Core/ExtrudeOptions.cs  (MODIFY)
```

Add one property:

```csharp
/// <summary>
/// When non-null, per-pixel depth mode. Overrides <see cref="Depth"/>.
/// </summary>
public DepthMap? DepthMap { get; init; } = null;
```

`VoxelSize` stays in the record (CLI parity). Web always passes 1.0 and no longer
exposes the control in UI.

### 1.3 `Extruder` — route to per-pixel path

```
Mixel.Core/Extruder.cs  (MODIFY)
```

`Build()` private method:

```csharp
private static (Mesh mesh, byte[] tex) Build(ExtrudeOptions o)
{
    var img  = PngLoader.Load(o.PngBytes);
    ImageSize.Validate(img.Width, img.Height, o.AllowNonStandardSize);
    var tex  = TextureBaker.BakePng(img, SilhouetteMask.Build(img));  // tex unchanged
    var mesh = o.DepthMap is not null
        ? MeshBuilder.BuildFromDepthMap(o.DepthMap, o.VoxelSize, o.Pivot)
        : MeshBuilder.Build(SilhouetteMask.Build(img), o.Depth, o.VoxelSize, o.Pivot);
    return (mesh, tex);
}
```

> Note: `SilhouetteMask.Build` is called twice only when `DepthMap` is null (simple
> mode). Acceptable; it's cheap. If profiling shows it matters, cache the mask in a
> local.

### 1.4 `MeshBuilder` — `BuildFromDepthMap` overload

```
Mixel.Core/MeshBuilder.cs  (MODIFY)
```

New public entry point alongside the existing `Build`:

```csharp
public static Mesh BuildFromDepthMap(DepthMap dm, float voxelSize, Pivot pivot)
```

**Back face (z = 0):** Greedy maximal rectangles over all pixels where `dm.At(x,y) > 0`.
Same algorithm as current; depth differences are irrelevant here.

**Front faces (z = level):** Group solid pixels by level. Within each level group run
the same greedy-rect pass; emit quads at `z = level * voxelSize`.

**Side walls:** For each solid pixel `(x,y)` with depth `D`, for each cardinal
neighbor `(nx,ny)` with depth `ND`:

- If `D > ND` (neighbor is shallower or air): emit wall quad on `(x,y)`'s side
  spanning `z = ND * voxelSize` to `z = D * voxelSize`.
- If `D <= ND`: the neighbor will emit the wall on its own side — skip.

This produces correct stairstepped walls matching KenneyShape output.

**Pivot:** Uses `dm.MaxLevel` in place of `depth` for offset calculations.

---

## 2. Web (`Mixel.Web`)

### 2.1 `FileItem` — depth state per file

```
Mixel.Web/Services/FileItem.cs  (MODIFY)
```

Add:

```csharp
/// <summary>Null until per-pixel mode activates for this file.</summary>
public byte[]? DepthLevels { get; set; }
public int DepthWidth      { get; set; }
public int DepthHeight     { get; set; }
```

Initialization (from `Home.razor` when switching to per-pixel mode): read alpha channel
of `Bytes`, set `DepthLevels[i] = img[i].A == 255 ? (byte)1 : (byte)0`, cropped to
the same bbox as `SilhouetteMask` would produce.

### 2.2 `ExtrudeSettings` — mode + max depth

```
Mixel.Web/Services/ExtrudeSettings.cs  (MODIFY)
```

Add:

```csharp
public bool PerPixelMode   { get; set; } = false;
public int  MaxDepthLevels { get; set; } = 16;  // choices: 8, 16, 24, 32
```

`VoxelSize` stays in the class; web always leaves it at 1.0.

`ToOptions(byte[] png, FileItem? item)` overload: if `PerPixelMode && item?.DepthLevels != null`,
build a `DepthMap` from `item.DepthLevels` and attach it to `ExtrudeOptions`.

### 2.3 New `DepthPainterState.cs`

```
Mixel.Web/Services/DepthPainterState.cs  (NEW)
```

Pure C# service (no Blazor dependencies). Owns tool logic:

```csharp
public enum PainterTool { Paint, Erase, Eyedrop, Fill }

public sealed class DepthPainterState
{
    public PainterTool Tool        { get; set; } = PainterTool.Paint;
    public byte        ActiveLevel { get; set; } = 1;

    // Apply tool at pixel (x,y). Returns true if DepthLevels was mutated.
    public bool Apply(byte[] levels, int w, int h, int x, int y);
}
```

Tool behaviour:

| Tool | Action |
|------|--------|
| Paint | `levels[y*w+x] = ActiveLevel` (no-op if pixel is air — `levels[i]` was 0 before mode activated) |
| Erase | `levels[y*w+x] = 0` |
| Eyedrop | `ActiveLevel = levels[y*w+x]`; returns false (no mutation) |
| Fill | 4-connected BFS from `(x,y)` replacing cells matching `levels[y*w+x]` with `ActiveLevel` |

> Paint guard: "air" is defined as the pixel having 0 in the initialized DepthLevels
> (transparent in source PNG). Painting an air pixel is a no-op.

### 2.4 New `DepthPainter.razor`

```
Mixel.Web/Components/DepthPainter.razor  (NEW)
```

Canvas component:

- `<canvas id="depth-canvas">` sized to `DepthWidth × DepthHeight` (CSS-scaled up with
  `image-rendering: pixelated` for visibility).
- On mount: `initDepthCanvas(id, pngBytes, w, h)` draws the source PNG as background.
- After every paint op: `renderDepthOverlay(id, levels, w, h, maxDepth)` draws
  semi-transparent hue-per-level swatches atop.
- `listenCanvasInput(id, dotNetRef)` attaches `pointerdown`/`pointermove`/`pointerup`;
  calls back `OnCanvasInput(x, y, buttons)` (pixel-space coords, `buttons` bitmask).
- Depth palette: a row of `MaxDepthLevels` swatches below the canvas. Click → set
  `ActiveLevel`. Selected swatch has a highlight ring.
- Tool selector: four icon buttons (Paint ✏️ / Fill 🪣 / Eyedrop 🔍 / Erase ✕).

### 2.5 `mixel.js` — three new interop functions

```
Mixel.Web/wwwroot/js/mixel.js  (MODIFY)
```

```js
initDepthCanvas(id, pngBytes, w, h)       // draw source PNG via ImageData
renderDepthOverlay(id, levels, w, h, max) // per-level hue overlay
listenCanvasInput(id, dotNetRef)          // pointer events → invokeMethodAsync
```

All functions are additive; no existing functions change.

### 2.6 `OptionsPanel` / `OptionsWidget` changes

```
Mixel.Web/Components/OptionsPanel.razor   (MODIFY)
Mixel.Web/Components/OptionsWidget.razor  (MODIFY)
```

- **Remove** Voxel Size field entirely.
- **Add** Mode toggle: `Simple | Per-pixel` (a two-button toggle or a `<select>`).
- **Simple mode:** Depth stepper unchanged (1..`DepthMax`, currently 3; keep same).
- **Per-pixel mode:** MaxDepthLevels `<select>` with options 8 / 16 / 24 / 32.

### 2.7 `Home.razor` — mode-aware stage

```
Mixel.Web/Pages/Home.razor  (MODIFY)
```

Stage behaviour by mode:

| Mode | Normal view | Side-by-side view |
|------|-------------|-------------------|
| Simple | `ModelPreview` only (no change) | N/A |
| Per-pixel | `DepthPainter` + "Preview 3D" button swaps to `ModelPreview` | `DepthPainter` left · `ModelPreview` right |

- Layout toggle button (⬛/⬜ or similar) in stage header; visible only in per-pixel mode.
- Side-by-side via `display: grid; grid-template-columns: 1fr 1fr`.
- `RegenAsync()`: if `PerPixelMode && currentItem.DepthLevels != null`, builds
  `DepthMap` and passes it via `ExtrudeSettings.ToOptions(bytes, item)`.
- Switching files in per-pixel mode: if target `FileItem.DepthLevels` is null,
  initialize it from the file's PNG before showing the painter.

---

## 3. Testing

All .NET logic fully covered. No Playwright/E2E (manual acceptance per existing policy).

### 3.1 `tests/Mixel.Tests/DepthMapTests.cs` (NEW)

- `At()` returns 0 for out-of-bounds coords.
- `MaxLevel` returns correct max over `Levels` array.
- `At()` correct for in-bounds coords.

### 3.2 `tests/Mixel.Tests/DepthMapMeshTests.cs` (NEW)

- **Regression:** uniform depth map (all levels = 1) → geometry equivalent to `MeshBuilder.Build` with `depth=1`.
- **Step wall:** adjacent pixels at levels 1 and 3 → wall quad spans z=1 to z=3.
- **Air neighbor:** solid pixel adjacent to air → full wall from z=0 to z=level.
- **Same-level adjacency:** no wall emitted between two adjacent pixels at same level.
- **Front face grouping:** two same-level non-adjacent pixels each get their own front quad.
- **Back face merge:** all solid pixels regardless of level share a common z=0 back.
- **Pivot BottomCenter:** offset uses `MaxLevel` as depth.
- **Pivot Center / MinCorner:** offsets correct.
- **Empty depth map:** throws `EmptySilhouetteException` (or equivalent).

### 3.3 `tests/Mixel.Tests/ExtruderDepthMapTests.cs` (NEW)

- `ExtrudeGlb` with non-null `DepthMap` produces non-empty bytes without error.
- `ExtrudeGlb` with null `DepthMap` uses simple path (existing behaviour preserved).
- `ExtrudeToMemory` with `DepthMap` produces correct file names per format.

### 3.4 `tests/Mixel.Web.Tests/DepthPainterStateTests.cs` (NEW)

- `Paint` writes `ActiveLevel` to correct index.
- `Paint` on an air pixel (level 0 in initialized map) is a no-op.
- `Erase` sets index to 0.
- `Eyedrop` sets `ActiveLevel` to the pixel's level; returns false (no mutation).
- `FloodFill` fills 4-connected region of same level; stops at different-level boundary.
- `FloodFill` does not cross diagonal corners.
- `FloodFill` on single isolated pixel fills only that pixel.
- `FloodFill` no-op when `ActiveLevel == current level`.

### 3.5 `tests/Mixel.Web.Tests/ExtrudeSettingsTests.cs` (NEW)

- `ToOptions` with `PerPixelMode=false` → `DepthMap` is null, `Depth` set correctly.
- `ToOptions` with `PerPixelMode=true` and valid `FileItem.DepthLevels` → `DepthMap` non-null, dimensions match.
- `ToOptions` with `PerPixelMode=true` and null `DepthLevels` → falls back to simple path.

### 3.6 `tests/Mixel.Web.Tests/OptionsPanelTests.cs` (MODIFY)

- Remove voxel-size binding tests.
- Add: mode toggle switches `Settings.PerPixelMode`.
- Add: MaxDepthLevels select renders options 8/16/24/32; selecting 24 updates `Settings.MaxDepthLevels`.
- Add: Simple mode shows Depth stepper; Per-pixel mode hides it.

### 3.7 `tests/Mixel.Web.Tests/ExtrusionServiceTests.cs` (MODIFY)

- Add: `PreviewGlb` with a `FileItem` carrying `DepthLevels` returns non-empty bytes.

---

## 4. File Change Summary

| File | Status |
|------|--------|
| `Mixel.Core/DepthMap.cs` | NEW |
| `Mixel.Core/ExtrudeOptions.cs` | MODIFY |
| `Mixel.Core/MeshBuilder.cs` | MODIFY |
| `Mixel.Core/Extruder.cs` | MODIFY |
| `Mixel.Web/Services/FileItem.cs` | MODIFY |
| `Mixel.Web/Services/ExtrudeSettings.cs` | MODIFY |
| `Mixel.Web/Services/DepthPainterState.cs` | NEW |
| `Mixel.Web/Components/DepthPainter.razor` | NEW |
| `Mixel.Web/Components/OptionsPanel.razor` | MODIFY |
| `Mixel.Web/Components/OptionsWidget.razor` | MODIFY |
| `Mixel.Web/Pages/Home.razor` | MODIFY |
| `Mixel.Web/wwwroot/js/mixel.js` | MODIFY |
| `Mixel.Web/wwwroot/css/app.css` | MODIFY |
| `tests/Mixel.Tests/DepthMapTests.cs` | NEW |
| `tests/Mixel.Tests/DepthMapMeshTests.cs` | NEW |
| `tests/Mixel.Tests/ExtruderDepthMapTests.cs` | NEW |
| `tests/Mixel.Web.Tests/DepthPainterStateTests.cs` | NEW |
| `tests/Mixel.Web.Tests/ExtrudeSettingsTests.cs` | NEW |
| `tests/Mixel.Web.Tests/OptionsPanelTests.cs` | MODIFY |
| `tests/Mixel.Web.Tests/ExtrusionServiceTests.cs` | MODIFY |

---

## 5. Implementation Notes for Agents

- **Caveman mode required** for all agents (saves context).
- **Context7** for all library docs: SharpGLTF, Blazor WASM, bUnit, xUnit.
- Each task spawns a fresh agent via `superpowers:subagent-driven-development`.
- Agents must commit after each task with Conventional Commit messages.
- No agent touches CLI (`Mixel.Cli`) or `ImageSize` validation.
