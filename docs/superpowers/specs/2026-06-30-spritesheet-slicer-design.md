# Spritesheet Slicer — Design

- **Date:** 2026-06-30
- **Status:** Approved (autonomous brainstorming; clarifying answers supplied up-front)
- **Scope:** Web + Core only. No CLI changes.

## 1. Problem & Goal

Users who only have a **spritesheet** (a single PNG containing many small sprites
packed into a grid or atlas) currently have no way to work with the individual
sprites inside Mixel. They must extract each sprite by hand in an external editor
before uploading.

**Goal:** Let a user upload a spritesheet, draw named rectangular selections over
it, and send each named selection into the existing editor as if it had been
uploaded as a standalone PNG. The spritesheet itself never becomes a `FileItem`;
only the crops do.

### Success criteria

1. User can open a "slice a spritesheet" entry point and pick a PNG.
2. User can rubber-band rectangles on the rendered sheet to define sprites.
3. Each rectangle gets an editable name (default `sprite_01`, `sprite_02`, …).
4. Rectangles cannot overlap; minimum size is 1×1 px.
5. On confirm, each named rectangle is cropped to PNG bytes and added to
   `Home._files` as a `FileItem` — identical downstream behaviour to uploading
   that crop as a PNG (preview, depth painting, export all work unchanged).
6. The feature is fully isolated: no existing extrusion, depth, export, or file
   logic is modified in a way that changes current behaviour.

### Non-goals (v1)

- Re-opening the slicer to edit a previously sliced sheet (once extracted, crops
  are ordinary `FileItem`s).
- Automatic grid detection / auto-slicing.
- Rotating, padding, or trimming sprites.
- Persisting the spritesheet or regions across sessions.

## 2. Constraints & Assumptions

| Topic | Decision |
| --- | --- |
| Entry point | A non-intrusive `link-btn` ("Slice a spritesheet →") in `FilePanel`, beneath the existing upload control, with its own hidden single-file `InputFile`. |
| Sprites per sheet | 4–256 typical; design must stay responsive at that scale. |
| Drawing | Canvas rubber-band drag on the rendered sheet (mirrors depth-canvas interop). |
| Naming | Inline text input per region in a side list; auto-default `sprite_NN`. |
| Overlap | Disallowed. A new rectangle that intersects an existing one is rejected with a brief inline notice. |
| Sprite → editor | Each crop becomes a `FileItem` via the existing `Home.HandleUpload` path. The sheet is **not** added. |
| Min crop | 1×1 px. Non-standard sizes are allowed but flagged (honours `ExtrudeSettings.AllowNonStandardSize`); flagging is a non-blocking hint. |
| Re-open slicer | Not required for v1. |
| Core change | New pure `SpritesheetSlicer.Crop(RgbaImage, SpriteRect) → byte[]` plus a `SpriteRect` value type. |

## 3. Architecture Overview

The feature is split across the two layers along the existing seam:

```
Core (pure, testable, net8.0)
  SpriteRect            value type: X, Y, Width, Height (source-pixel space) + Intersects/Contains/bounds helpers
  SpritesheetSlicer     static: Crop(RgbaImage source, SpriteRect rect) -> byte[] (PNG, via StbImageWriteSharp)

Web (Blazor WASM, net10.0)
  Services/SliceRegion.cs     record: Name + SpriteRect (one defined sprite)
  Services/SlicerState.cs     plain C# logic: region list, auto-naming, overlap reject, rename/remove,
                              BuildExtract(RgbaImage) -> List<(name, png bytes)>, batch name de-dup
  Components/SlicerDialog.razor  modal overlay: thin view over SlicerState + JS interop
  Interop/MixelJs.cs          + InitSlicerAsync / RenderSlicerRegionsAsync / DisposeSlicerAsync wrappers
  wwwroot/js/mixel.js         + initSlicer / renderSlicerRegions / disposeSlicer
  Pages/Home.razor            + conditional <SlicerDialog>; reuses HandleUpload for the crops
  Components/FilePanel.razor   + "Slice a spritesheet →" entry that raises OnImportSpritesheet
  wwwroot/css/app.css         + .slicer-* styles (overlay, panel, region list)
```

This mirrors the established **`DepthPainter` (view) + `DepthPainterState` (logic)**
pattern: all decision logic lives in `SlicerState` (no JS dependency, fully unit
tested); `SlicerDialog.razor` only wires DOM/JS to that state.

### Why a modal dialog rather than a stage "mode"

The stage is already shared between the depth painter and the 3D preview pip, and
its content is driven by the *selected `FileItem`*. The spritesheet is explicitly
**not** a `FileItem`, so it has no place in the selection model. A self-contained
modal keeps the slicer's lifecycle independent of `_selected`, `_settings`,
`PerPixelMode`, and the preview — which is exactly the isolation requirement.
The dialog is rendered only while `Home._sheetBytes != null`, so it adds zero
overhead and zero interaction with existing features when unused.

## 4. Core Layer Detail

### 4.1 `SpriteRect` (new, `Mixel.Core`)

```csharp
public readonly record struct SpriteRect(int X, int Y, int Width, int Height)
{
    public int Right  => X + Width;   // exclusive
    public int Bottom => Y + Height;  // exclusive
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Intersects(SpriteRect o)
        => X < o.Right && Right > o.X && Y < o.Bottom && Bottom > o.Y;

    public bool WithinBounds(int imageWidth, int imageHeight)
        => X >= 0 && Y >= 0 && Width > 0 && Height > 0
           && Right <= imageWidth && Bottom <= imageHeight;
}
```

Edges that merely touch (`Right == o.X`) do **not** count as intersecting, so
adjacent sprites packed flush against each other are allowed.

### 4.2 `SpritesheetSlicer` (new, `Mixel.Core`)

```csharp
public static class SpritesheetSlicer
{
    /// <summary>Crops <paramref name="rect"/> out of <paramref name="source"/>
    /// and encodes it as a PNG. Pure; no UI dependency.</summary>
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

This reuses the exact PNG-encoding approach already proven in `TextureBaker.BakePng`.
It is intentionally tiny, pure, and round-trippable for tests (`Crop` then
`PngLoader.Load` yields the original sub-region).

## 5. Web Layer Detail

### 5.1 `SliceRegion` (new, `Mixel.Web.Services`)

```csharp
public sealed class SliceRegion
{
    public required string Name { get; set; }   // user-editable, no extension
    public required SpriteRect Rect { get; init; }
}
```

### 5.2 `SlicerState` (new, `Mixel.Web.Services`) — the testable core of the feature

Responsibilities (all JS-free, all unit tested):

- Holds `List<SliceRegion> Regions`.
- `bool TryAdd(SpriteRect rect, out string? error)` — rejects empty (`< 1×1`) and
  rejects any rect intersecting an existing region; on success appends a region
  with the next auto-name and returns `true`.
- `string NextName()` — lowest unused `sprite_NN` (zero-padded to ≥2 digits),
  scanning existing names so re-numbering after deletes stays predictable.
- `void Remove(SliceRegion region)` and `void Rename(SliceRegion region, string name)`
  (rename trims; empty rename falls back to the auto-name; rename does **not**
  need uniqueness because export de-dups).
- `bool IsStandardSize(SliceRegion r)` — `ImageSize.IsStandard(r.Rect.Width, r.Rect.Height)`,
  used only for a non-blocking "non-standard" hint when `AllowNonStandardSize` is false.
- `List<(string name, byte[] bytes)> BuildExtract(RgbaImage sheet)` — for each
  region calls `SpritesheetSlicer.Crop`, names the output `"{deduped}.png"`,
  de-duplicating within the batch (`sprite_01`, `sprite_01_2`, …).

Keeping all of this outside the `.razor` file means the feature's behaviour is
verifiable without a browser, exactly like `DepthPainterState`.

### 5.3 `SlicerDialog.razor` (new, `Mixel.Web.Components`)

A modal overlay. Parameters:

```csharp
[Parameter] public byte[] SheetBytes { get; set; } = default!;
[Parameter] public string SheetName  { get; set; } = "spritesheet.png";
[Parameter] public bool AllowNonStandardSize { get; set; }
[Parameter] public EventCallback<IReadOnlyList<(string name, byte[] bytes)>> OnExtract { get; set; }
[Parameter] public EventCallback OnClose { get; set; }
```

Lifecycle:

1. **`OnInitialized`** — decode the sheet once: `_image = PngLoader.Load(SheetBytes)`.
   On `InvalidPngException`, set `_error` and render an error state with a close
   button (never throws into the render tree).
2. **`OnAfterRenderAsync(firstRender)`** — if decoded, build an RGBA byte buffer
   from `_image.Pixels` and call `Js.InitSlicerAsync("slicer-canvas",
   "slicer-overlay", rgba, w, h, _self)`. The base canvas draws the sheet (reusing
   the `initDepthCanvas` scale-to-fit approach); the overlay canvas captures
   rubber-band input.
3. **`[JSInvokable] OnRegionDrawn(int x, int y, int w, int h)`** — build a
   `SpriteRect`, call `_state.TryAdd`; on success re-render regions
   (`Js.RenderSlicerRegionsAsync`) and focus the new name input; on failure set a
   transient `_notice` (e.g. "Sprites can't overlap").
4. **Rename / Remove** from the side list → mutate `_state`, re-render regions.
5. **Confirm** ("Add N sprite(s) to editor", disabled when `Regions.Count == 0`)
   → `var crops = _state.BuildExtract(_image!); await OnExtract.InvokeAsync(crops);
   await OnClose.InvokeAsync();`.
6. **`DisposeAsync`** — `Js.DisposeSlicerAsync("slicer-overlay")` removes pointer
   listeners (prevents the listener-leak class of bug the codebase has fixed
   before) and disposes the `DotNetObjectReference`.

Layout: full-screen `.slicer-overlay` (fixed, scrim) containing a `.slicer-panel`
with a header (title + ✕), a left canvas area (`slicer-canvas` base +
`slicer-overlay` rubber-band/labels canvas, same two-canvas stack as the depth
painter), and a right `.slicer-regions` list (name input + size + delete per
region) ending in the confirm button. Styled entirely with existing CSS custom
properties.

### 5.4 JS interop additions (`mixel.js` + `MixelJs.cs`)

- **`initSlicer(baseId, overlayId, rgbaBytes, w, h, dotNetRef)`** — sizes/scales
  the base canvas and draws the RGBA buffer (same logic as `initDepthCanvas`);
  sizes the overlay canvas to the base's display dimensions; stores natural
  `{w,h}`; attaches rubber-band pointer handlers:
  - `pointerdown`: record start pixel (floored + clamped to `[0,w-1]/[0,h-1]`),
    set pointer capture.
  - `pointermove`: redraw cached committed regions + the live in-progress
    rectangle on the overlay (no .NET round-trip per move → smooth on large sheets).
  - `pointerup`: normalise to a natural-pixel rect with **inclusive** bounds
    (`width = |cx - sx| + 1`, etc.), clamp to image bounds, and invoke
    `dotNetRef.OnRegionDrawn(x, y, w, h)`.
  - Stores a cleanup closure on the canvas for `disposeSlicer`.
- **`renderSlicerRegions(overlayId, regions)`** — caches `regions` on the canvas,
  clears the overlay, and for each region strokes its outline and draws a small
  name label (same high-contrast text style as `renderDepthLabels`), mapping
  natural-pixel coords to display coords via the stored scale.
- **`disposeSlicer(overlayId)`** — runs the stored cleanup closure (removes
  listeners), clears cached state.

`MixelJs.cs` gains thin `InitSlicerAsync` / `RenderSlicerRegionsAsync` /
`DisposeSlicerAsync` wrappers matching the existing style.

### 5.5 `FilePanel.razor` change (entry point)

Add, directly under the existing upload control, a second hidden `InputFile`
(single file, `accept=".png"`) surfaced as a `link-btn` "Slice a spritesheet →".
Its change handler reads the one file (same `OpenReadStream` pattern, 64 MB cap)
and raises a new callback:

```csharp
[Parameter] public EventCallback<(string name, byte[] bytes)> OnImportSpritesheet { get; set; }
```

No existing `FilePanel` markup or callbacks are altered.

### 5.6 `Home.razor` change (wiring)

```csharp
private byte[]? _sheetBytes;
private string  _sheetName = "spritesheet.png";

private void OpenSlicer((string name, byte[] bytes) sheet)
{
    _sheetName  = sheet.name;
    _sheetBytes = sheet.bytes;
    StateHasChanged();
}

private void CloseSlicer() { _sheetBytes = null; StateHasChanged(); }

private async Task OnSlicerExtract(IReadOnlyList<(string name, byte[] bytes)> sprites)
{
    _sheetBytes = null;          // close
    await HandleUpload(sprites); // existing path: adds FileItems, selects, regen
}
```

Markup additions: pass `OnImportSpritesheet="OpenSlicer"` to `<FilePanel>`, and
render the dialog only when active:

```razor
@if (_sheetBytes is not null)
{
    <SlicerDialog SheetBytes="_sheetBytes" SheetName="_sheetName"
                  AllowNonStandardSize="_settings.AllowNonStandardSize"
                  OnExtract="OnSlicerExtract" OnClose="CloseSlicer" />
}
```

`HandleUpload` already handles the empty-list/first-select/per-pixel-init/regen
concerns, so extracted sprites behave exactly like uploaded PNGs. No existing
`Home` method is modified.

## 6. Data Flow

```
FilePanel "Slice a spritesheet →"  --(name,bytes)-->  Home.OpenSlicer  --> _sheetBytes set
   -> <SlicerDialog> renders
        OnInitialized: PngLoader.Load(SheetBytes) -> _image (RgbaImage)
        OnAfterRender: Js.InitSlicer(rgba,w,h)            -> sheet drawn on canvas
   user drags rectangle
        JS pointerup -> OnRegionDrawn(x,y,w,h)
            -> SlicerState.TryAdd -> Regions updated
            -> Js.RenderSlicerRegions(regions)            -> outlines + labels drawn
   user edits names / deletes regions  -> SlicerState mutated -> re-render
   user clicks "Add N sprites to editor"
        -> SlicerState.BuildExtract(_image)               -> List<(name.png, pngBytes)>
        -> OnExtract -> Home.OnSlicerExtract -> HandleUpload(sprites)
            -> new FileItem per crop in _files            -> identical to PNG upload
        -> dialog closes
```

## 7. Error Handling

| Situation | Behaviour |
| --- | --- |
| Non-PNG / corrupt sheet | `SlicerDialog` catches `InvalidPngException` on init, shows an error message + close button. Nothing is added. |
| Rectangle overlaps existing | `SlicerState.TryAdd` returns `false`; dialog shows transient `_notice`; no region added. |
| Zero-size drag (click) | `TryAdd` rejects (`< 1×1`); silently ignored. |
| Drag past image edges | JS clamps to image bounds before invoking `OnRegionDrawn`; `Crop` also validates defensively (`ArgumentOutOfRangeException`). |
| Non-standard crop size, `AllowNonStandardSize == false` | Non-blocking "non-standard" hint on that region. Extraction still allowed; the editor already surfaces size issues on preview/export. |
| Duplicate sprite names | `BuildExtract` de-dups within the batch (`name`, `name_2`, …). |
| Confirm with no regions | Button disabled. |

## 8. Testing Strategy

**Core (`tests/Mixel.Tests`, xUnit):**

- `SpriteRectTests` — `Intersects` (overlap true, flush-adjacent false, contained
  true, disjoint false); `WithinBounds` (inside, edge-exact, overflow,
  negative origin); `IsEmpty`.
- `SpritesheetSlicerTests` — crop dimensions match the rect; round-trip pixels
  (`Crop` then `PngLoader.Load` equals `source.At(offset+...)`) using
  `TestImages.FromAscii`; 1×1 crop; full-image crop; corner/edge crops;
  out-of-bounds throws.

**Web (`tests/Mixel.Web.Tests`, xUnit + bUnit):**

- `SlicerStateTests` (no JS) — auto-naming sequence and gap re-use; `TryAdd`
  overlap rejection and empty rejection; rename trim/empty-fallback; remove;
  `BuildExtract` produces one PNG per region with correct `.png` names and batch
  de-dup; `IsStandardSize`.
- `SlicerDialog` bUnit smoke test (`JSRuntimeMode.Loose`, like `HomeRenderTests`)
  — renders header/confirm; confirm disabled with no regions; error state renders
  for invalid PNG bytes. (Pointer drawing needs real JS and is out of bUnit scope,
  consistent with how `DepthPainter` is treated.)

All new tests are additive; existing suites are untouched.

## 9. Isolation Guarantees (no-conflict checklist)

- New Core types (`SpriteRect`, `SpritesheetSlicer`) — no edits to existing Core.
- New Web files (`SliceRegion`, `SlicerState`, `SlicerDialog.razor`) — additive.
- `MixelJs.cs` / `mixel.js` — only new functions appended; no existing function
  changed.
- `FilePanel.razor` — one new `InputFile` + callback added; existing upload,
  bulk, and list markup unchanged.
- `Home.razor` — additive fields, three small methods, one conditional render
  block, one new `FilePanel` parameter; no existing method body changed.
- `app.css` — only new `.slicer-*` rules appended.
- Extrusion, depth-map, export, theming code: untouched.

## 10. Open Questions

None. All clarifying questions were resolved up-front (see §2). Re-opening the
slicer and auto-grid detection are explicitly deferred (§1 non-goals).
