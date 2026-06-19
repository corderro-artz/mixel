# mixel — PNG Pixel Art → Z-Extruded glTF — Design Spec

**Date:** 2026-06-19
**Status:** Approved (design); pending spec review
**Repo:** `mixel` (greenfield, MIT)

## 1. Summary

`mixel` is a library (`Mixel.Core`) exposed through a thin command-line utility
(`mixel`) that takes a transparency-supporting PNG of pixel art and extrudes the
opaque pixels along the Z axis into a 3D model, in the manner of Kenney's
Kenshape. The model is exported as a glTF 2.0 asset (default `.glb`) suitable for
dragging directly into modeling software and game engines (Blender, Unreal).

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
- Decisions grounded in verified facts (library versions, glTF 2.0 spec), not
  assumptions.

### Non-Goals (first version)
- No formats other than PNG in / glTF out.
- No per-pixel variable height/depth, bevels, or rounded edges (Kenshape's
  advanced shaping). Uniform depth only.
- No translucency in output (see alpha rule).
- No config file. All behavior is controlled by CLI flags/args; users wanting
  repeatability write a small script.
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
- `Mixel.Core` — class library. Public entry point:
  `Extruder.Extrude(ExtrudeOptions) → ExtrudeResult` (bytes or written file).
- `Mixel.Cli` — thin executable (`mixel`) that parses args and calls
  `Mixel.Core`. No business logic beyond wiring + error reporting.
- `Mixel.Tests` — xUnit test project.

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
mixel <input.png> [options]

Options:
  -o, --output <path>          Output file (default: <input> with chosen ext)
  -d, --depth <N>              Extrusion depth in voxels (int, default 1, min 1)
  -s, --voxel-size <F>         Unit length per voxel (float, default 1.0, > 0)
  -f, --format <fmt>           glb | gltf | gltf-embedded (default glb)
  -p, --pivot <mode>           bottom-center | center | min-corner
                               (default bottom-center)
      --help                   Show help
      --version                Show version
```

- Parser: `System.CommandLine`.
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

## 13. Open Questions / Future (out of scope now)

- Additional input formats (e.g. BMP, GIF) and output formats (OBJ, FBX, USD).
- Per-pixel variable depth / heightmap-driven extrusion; bevels.
- Optional mesh optimization passes (weld/dedup/Draco/KTX) — easier path exists
  if ever ported to glTF-Transform.
- Translucency preservation via glTF BLEND materials.
- Configurable alpha threshold if non-hard-edged art becomes a use case.
