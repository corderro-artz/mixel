# mixel — PNG Pixel Art → Z-Extruded glTF — Design Spec

**Date:** 2026-06-19
**Status:** Approved (design); pending spec review
**Repo:** `mixel` (greenfield, MIT)

## 1. Summary

`mixel` is a library (`Mixel.Core`) that takes a transparency-supporting PNG of
pixel art and extrudes the opaque pixels along the Z axis into a 3D model, in the
manner of Kenney's Kenshape. The model is exported as a glTF 2.0 asset (default
`.glb`) suitable for dragging directly into modeling software and game engines
(Blender, Unreal).

The core is surfaced through **two** front ends, both of which expose the full
feature set:
- **Part A — CLI** (`mixel`): a thin command-line utility over `Mixel.Core`.
- **Part B — Web app** (`Mixel.Web`): a Blazor WebAssembly **PWA** — progressive,
  local-first, fully offline after install — that reuses `Mixel.Core` compiled to
  WASM. No logic duplication; both front ends call the identical core.

This spec is organized as: shared core (§3–§8, §10–§11), Part A CLI (§9),
distribution (§12), Part B web app (§13), and build sequencing (§14).

Scope is deliberately minimized for the first version:
- **Input:** `.png` only.
- **Output:** glTF 2.0 only (`.glb` default; `.gltf` and embedded `.gltf` via flags).

## 2. Goals & Non-Goals

### Goals
- Faithful flat extrusion of pixel art: each opaque pixel becomes part of a slab
  `--depth N` full voxels deep along Z.
- Single self-contained output file by default for frictionless drag-drop.
- Crisp pixel-art appearance (nearest-neighbor texture, no color bleed).
- Clean import: correct orientation, scale, and pivot with an identity node
  transform.
- **Batch processing** of many images (multiple files or a directory) from one
  invocation, shared by both front ends.
- **Web app** that exposes the full feature set, is installable, and works fully
  offline after install.
- Decisions grounded in verified facts (library versions, glTF 2.0 spec, brand
  colors), not assumptions.

### Non-Goals (first version)
- No formats other than PNG in / glTF out.
- No per-pixel variable height/depth, bevels, or rounded edges (Kenshape's
  advanced shaping). Uniform depth only.
- No translucency in output (see alpha rule).
- No config file. All behavior is controlled by CLI flags/args; users wanting
  repeatability write a small script.
- No server, account, network call, or telemetry in the web app — it is
  local-first and offline-capable by requirement.
- No GUI.

## 3. Verified Facts (research basis)

- **glTF 2.0** uses a right-handed, **Y-up** coordinate system; distances are in
  meters; two encodings exist: `.gltf` (JSON) and `.glb` (binary). `.glb` is
  glTF 2.0, just the single-file binary form.
- **NEAREST** texture filtering = sampler `magFilter`/`minFilter` value `9728`.
- **SharpGLTF 1.0.6** (`SharpGLTF.Core` / `SharpGLTF.Toolkit`, NuGet) — actively
  maintained .NET glTF 2.0 reader/writer with a builder API and built-in
  validation; supports `.glb` and `.gltf`.
  https://www.nuget.org/packages/SharpGLTF.Core
- **StbImageSharp** + **StbImageWriteSharp** — public-domain, pure-managed PNG
  decode/encode; no native dependencies; NativeAOT-friendly. Chosen over
  ImageSharp (v3+ Six Labors Split License has commercial-use friction) and
  SkiaSharp (native dependency). ImageSharp/SkiaSharp remain documented swappable
  alternatives behind the loader/baker interfaces.
- **.NET** supports self-contained single-file publish
  (`-p:PublishSingleFile=true --self-contained`) and NativeAOT for per-RID
  drag-to-run executables.

## 4. Architecture

A linear pipeline of small, independently testable units. Only the loader,
texture baker, and writer touch third-party dependencies; the mask and mesh
stages are pure.

```
PNG bytes
  → PngLoader        decode → RGBA grid                 (StbImageSharp)
  → SilhouetteMask   alpha==255 → bool mask + crop bbox (pure)
  → MeshBuilder      mask + depth + voxelSize → Mesh    (pure — the core)
  → TextureBaker     crop RGBA to bbox → PNG bytes      (StbImageWriteSharp)
  → GltfWriter       mesh + texture + transform → bytes (SharpGLTF)
```

### Projects
- `Mixel.Core` — class library (`netstandard2.1`/`net*` multi-target so it loads
  in both the CLI runtime and the Blazor WASM runtime). Public entry points:
  `Extruder.Extrude(ExtrudeOptions) → ExtrudeResult` (single image) and
  `Extruder.ExtrudeBatch(IEnumerable<ExtrudeOptions>) → BatchResult`. Pure-managed
  deps only (SharpGLTF, StbImageSharp/Write) so it runs unchanged under WASM.
- `Mixel.Cli` — thin executable (`mixel`) that parses args and calls
  `Mixel.Core`. No business logic beyond wiring + error reporting.
- `Mixel.Web` — Blazor WebAssembly PWA front end (see §14). References
  `Mixel.Core` directly.
- `Mixel.Tests` — xUnit test project.

`ExtrudeOptions` is the single shared options record (input bytes/path, depth,
voxelSize, format, pivot, output target). Both front ends construct it; neither
duplicates extrusion logic.

### Unit responsibilities (what / how-used / depends-on)
| Unit | Does | Depends on |
|------|------|-----------|
| `PngLoader` | Decode PNG → `RgbaImage { width, height, pixels[] }` | StbImageSharp |
| `SilhouetteMask` | Apply opaque-only alpha rule → `bool[,]` mask; compute tight bounding box | none (pure) |
| `MeshBuilder` | Build extruded `Mesh` (positions, normals, UVs, indices) | none (pure) |
| `TextureBaker` | Crop source RGBA to bbox → PNG bytes | StbImageWriteSharp |
| `GltfWriter` | Assemble SharpGLTF model, apply pivot/scale, serialize to chosen format | SharpGLTF |
| `Extruder` | Orchestrate the pipeline | the above |
| `Cli` | Parse flags, invoke `Extruder`, map errors to exit codes | System.CommandLine |

## 5. Alpha Rule (input → solid)

**Opaque-only.** A pixel becomes a solid voxel **iff its alpha == 255**. Any
partial transparency (anti-aliased/feathered edges) is treated as empty. No
threshold flag. Rationale: pixel art is typically hard-edged; this is the
strictest, simplest, predictable reading.

If the resulting mask has **zero** opaque pixels, this is an error (exit 4).

## 6. Geometry (MeshBuilder — core)

- **Coordinate system:** glTF-standard Y-up, right-handed. The silhouette lies in
  the XY plane (image columns → +X, image rows → +Y with row 0 at top mapped so
  the art stands upright). It is extruded along **Z** by `depth` voxels; the art
  plane faces **+Z** (toward the viewer), so the model stands like an upright
  sprite.
- **Front (+Z) and back (−Z) faces:** greedy-mesh rectangular runs of opaque
  pixels into a small number of large quads. Because UV is an affine function of
  pixel position, a merged rectangle's corner UVs map to the correct texels.
  The back face reuses the same texels (mirrored winding/UV).
- **Perimeter walls (±X / ±Y, spanning the full depth):** a wall quad is emitted
  for every boundary edge — an opaque pixel face adjacent to a transparent pixel
  **or** the image edge. Interior holes (transparent pixels enclosed by opaque)
  therefore produce interior walls. Each wall quad's UVs map to the **owning
  pixel's texel center** (flat per-pixel color). Consecutive collinear wall quads
  sharing the same texel may be run-merged.
- **Normals:** flat per face (front +Z, back −Z, walls along their ±X/±Y axis).
- **Watertight:** the surface fully encloses the volume (front + back + all
  perimeter/interior walls).
- **Special case `depth = 0` is invalid;** minimum `depth = 1` yields a
  single-voxel-thick slab.

## 7. Texture

- The source PNG, **tight-cropped to the silhouette bounding box**, is the single
  texture, embedded in the output.
- **NEAREST** min/mag filtering (sampler 9728) for crisp pixels.
- UVs target **texel centers** (`(col + 0.5)/w`, `(row + 0.5)/h`) so NEAREST
  sampling never bleeds between adjacent texels.

## 8. Transform & Units

- `--voxel-size` (default `1.0`) sets the edge length, in glTF units (meters), of
  one voxel. Geometry is generated at this scale.
- `--pivot` chooses the origin, applied as a **vertex offset** so the exported
  node transform remains identity (cleanest engine import):
  - `bottom-center` (default): X and Z centered on the silhouette, Y = 0 at base
    — "feet on the floor."
  - `center`: geometric center at origin on all axes.
  - `min-corner`: lowest −X/−Y/−Z corner at (0,0,0).

## 9. CLI Surface

```
mixel <input>... [options]

<input>  one or more of: a .png file, several .png files, or a directory.
         A directory processes every *.png inside it (see --recursive).

Options:
  -o, --output <path>          Single input: output file (default: <input> with
                               chosen ext). Batch: output DIRECTORY (default:
                               alongside each source). Created if missing.
  -d, --depth <N>              Extrusion depth in voxels (int, default 1, min 1)
  -s, --voxel-size <F>         Unit length per voxel (float, default 1.0, > 0)
  -f, --format <fmt>           glb | gltf | gltf-embedded (default glb)
  -p, --pivot <mode>           bottom-center | center | min-corner
                               (default bottom-center)
  -r, --recursive              When an input is a directory, descend subdirs
                               (mirrors tree under -o). Default: top level only.
      --help                   Show help
      --version                Show version
```

- Parser: `System.CommandLine`.
- **Batch model:** any combination of file/dir inputs is expanded to a flat list
  of PNG paths, then run through `Extruder.ExtrudeBatch`. The same per-image
  options apply to all. One failing image reports its error but does **not** abort
  the batch; the process exits non-zero if **any** image failed, zero if all
  succeeded. A one-line summary (`N succeeded, M failed`) is printed to stderr.
- `--format`:
  - `glb` → single binary `.glb` (geometry + texture embedded). **Default.**
  - `gltf` → JSON `.gltf` + external `.bin` + `.png` siblings.
  - `gltf-embedded` → single JSON `.gltf` with buffers/texture base64-embedded.
  All three reuse one `GltfWriter`; only the SharpGLTF serialization mode differs.
- Default output extension follows `--format` (`.glb` / `.gltf`).

## 10. Error Handling

All errors print a clear message to stderr and return a non-zero exit code:

| Code | Condition |
|------|-----------|
| 0 | Success |
| 2 | Input file missing or unreadable |
| 3 | Input is not a valid/decodable PNG |
| 4 | No opaque pixels (empty silhouette) |
| 5 | Invalid flag value (`depth < 1`, `voxel-size <= 0`, unknown format/pivot) |
| 6 | Output path unwritable |

## 11. Testing

- **Unit:**
  - `SilhouetteMask`: alpha rule correctness; bounding-box crop; all-transparent
    detection.
  - `MeshBuilder`: tiny fixtures (single pixel; 2×2 with one transparent corner;
    2×2 with an interior hole in a larger grid; L-shape) → assert exact
    vertex/triangle counts, watertightness, normal directions, UV ranges within
    [0,1] at texel centers.
  - Pivot math: each mode places the offset correctly.
  - `TextureBaker`: crop dimensions and pixel content match bbox.
  - Batch expansion: file/dir/multi-file inputs expand to the expected PNG list;
    `--recursive` toggles subdir descent; one bad image fails alone, batch
    continues, exit code reflects any failure.
- **Round-trip / golden:** export each fixture → re-import via SharpGLTF, run its
  built-in validation, assert accessor counts and mesh bounds. Optionally run the
  Khronos glTF-Validator in CI.
- **Manual acceptance checklist** (documented, not automated): drag a `.glb` into
  Blender and Unreal; confirm upright orientation, scale, pivot, and crisp
  texture.
- Framework: **xUnit**.

## 12. Distribution

Per-RID self-contained single-file executables (drag-to-run, no runtime install):

```
dotnet publish -c Release -r <rid> -p:PublishSingleFile=true --self-contained
# optionally NativeAOT for smaller/faster startup
```

Target RIDs initially: `win-x64`, `osx-arm64`, `linux-x64`.

The web app (§13) is published as static files and is independent of these
per-RID CLI binaries.

## 13. Part B — Web Front End (`Mixel.Web`)

### 13.1 Platform
- **Blazor WebAssembly** standalone app (no ASP.NET host). `Mixel.Core` and its
  pure-managed deps run **in the browser via WASM** — the exact same extrusion
  code as the CLI, no JavaScript port.
- **PWA:** web app manifest + service worker that precaches the app shell and the
  .NET WASM assets, so after first load the app **installs** and runs **fully
  offline, with zero network calls** (a hard requirement). No server, account, or
  telemetry.
- Distribution: `dotnet publish` produces static files (`wwwroot/`) hostable on
  any static host or run locally; nothing server-side.

### 13.2 Feature parity
The UI exposes **every** core feature the CLI does: input (single/multi/batch),
`depth`, `voxel-size`, `format` (glb / gltf / gltf-embedded), `pivot`, and
recursive batch. Same `ExtrudeOptions`, same results.

### 13.3 Layout (single-screen, live panel)
- **Top bar:** `mixel` wordmark, `PWA · offline` badge, **theme picker**, theme
  quick-toggle, `Export` (with format dropdown).
- **Left:** drop zone accepting one PNG, many PNGs, or a folder (File System
  Access API where available, `<input type=file webkitdirectory>` fallback) +
  selectable batch file list.
- **Center:** **live 3D preview** via the `<model-viewer>` web component (bundled
  locally so it works offline). Regenerates the `.glb` in-memory on option change
  and displays it; orbit/zoom.
- **Right:** options panel — depth, voxel-size, format (segmented), pivot,
  recursive batch toggle.
- Responsive: panels stack on narrow viewports.

### 13.4 Input / output in the browser
- Decode dropped PNG bytes → `Mixel.Core` → `.glb`/`.gltf` bytes in memory.
- Single image → download the file (and feed the in-memory `.glb` to the
  preview). Batch → file list with per-item status; **Export all** zips the
  outputs (`System.IO.Compression` in WASM) and downloads one `.zip`.
- All in-browser; no upload.

### 13.5 Theming — 14 themes, Vaporsoft Dark default
- Implemented as CSS custom properties switched by a `data-theme` attribute on
  the root element. A small **theme registry** (name, mode, token values) drives
  both the picker and the applied vars.
- **Default: Vaporsoft Dark.** Selection persists in `localStorage`;
  `prefers-color-scheme` only influences the **first-run** pick between the two
  brand themes. Picker is grouped Dark / Light; a quick-toggle flips to the
  paired brand theme.
- **Brand themes (exact, from vaporsoft.dev live CSS):**
  - **Vaporsoft Dark** (default) — bg `#07080b` (ink), text `#f2ede6` (paper),
    accent `#a11f31` (carmine), border `rgba(242,237,230,.14)`.
  - **Vaporsoft Light** — bg `#f4efe8`, text `#121318`, accent `#a11f31`,
    border `rgba(18,20,24,.12)`.
- **6 dark alternates:** Midnight `#6366f1` on `#0f1115` · Carbon `#d4d4d4` on
  `#0e0e0e` · Forest `#34d399` on `#0c1410` · Ember `#f59e0b` on `#14100c` ·
  Synthwave `#ec4899` on `#16091f` · Ocean `#22d3ee` on `#08141c`.
- **6 light alternates:** Daylight `#2563eb` on `#ffffff` · Linen `#b45309` on
  `#faf6ef` · Mint `#059669` on `#f2faf5` · Rose `#db2777` on `#fdf2f6` ·
  Slate `#0ea5e9` on `#f1f5f9` · Sand `#ca8a04` on `#f7f1e6`.
  (Each theme also defines panel, muted, border, and stage-grid tokens; see the
  theme registry in implementation. Full token sets validated in the brainstorm
  mockup `.superpowers/brainstorm/.../layout-vaporsoft.html`.)

### 13.6 Web testing
- Core logic is already covered by `Mixel.Tests` (runs identically; the WASM
  runtime executes the same IL).
- UI: bUnit component tests for the options panel (flag ↔ `ExtrudeOptions`
  binding), theme switching (correct `data-theme` + persisted value), and batch
  list state.
- Smoke: a Playwright check that the published PWA loads offline (service worker
  registered), a PNG drop yields a downloadable `.glb`, and theme persists across
  reload. (Manual acceptance for install + true offline.)

## 14. Build Sequencing

The web app depends on the core, so build order is:

1. `Mixel.Core` (engine: loader → mask → mesh → texture → writer → batch) with
   `Mixel.Tests` green.
2. `Mixel.Cli` over the finished core.
3. `Mixel.Web` (Blazor WASM PWA shell, theming, preview, batch UI) over the
   finished core.

Steps 2 and 3 are independent of each other once step 1 exists → they are the
**parallel** work: at implementation time, dispatch parallel agents for the CLI
and the web app (and, within the web app, theming vs. preview vs. batch UI can be
further parallelized). No build agents are dispatched before step 1 exists.

## 15. Open Questions / Future (out of scope now)

- Additional input formats (e.g. BMP, GIF) and output formats (OBJ, FBX, USD).
- Per-pixel variable depth / heightmap-driven extrusion; bevels.
- Optional mesh optimization passes (weld/dedup/Draco/KTX) — easier path exists
  if ever ported to glTF-Transform.
- Translucency preservation via glTF BLEND materials.
- Configurable alpha threshold if non-hard-edged art becomes a use case.
