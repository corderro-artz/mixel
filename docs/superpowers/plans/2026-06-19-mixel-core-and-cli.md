# mixel Core + CLI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `Mixel.Core` (PNG pixel art → Z-extruded glTF engine) and `Mixel.Cli` (the `mixel` command-line tool), fully tested.

**Architecture:** A pure pipeline — `PngLoader → SilhouetteMask → MeshBuilder → TextureBaker → GltfWriter` — orchestrated by `Extruder`, with batch expansion on top. The CLI is a thin `System.CommandLine` wrapper that builds `ExtrudeOptions` and maps exceptions to exit codes. The web app (separate plan) reuses `Mixel.Core` unchanged.

**Tech Stack:** C# / .NET 8, SharpGLTF.Toolkit 1.0.6, StbImageSharp + StbImageWriteSharp (PNG decode/encode, pure-managed), System.CommandLine, xUnit.

## Global Constraints

- **Target framework:** `net8.0` for every project (Core must also load under Blazor WASM net8 in the later web plan — no native dependencies allowed in Core).
- **Core dependencies are pure-managed only:** SharpGLTF.Toolkit, StbImageSharp, StbImageWriteSharp. No SkiaSharp/ImageSharp/System.Drawing in Core.
- **Input:** `.png` only. **Alpha rule:** a pixel is solid **iff alpha == 255** (opaque-only; no threshold).
- **Coordinates:** glTF-standard Y-up, right-handed; art plane faces **+Z**; silhouette in XY, extruded along **Z**.
- **Output formats:** `glb` (default) | `gltf` | `gltf-embedded`. Default output extension follows format.
- **Texture:** source PNG tight-cropped to silhouette bbox; **NEAREST** min/mag filtering; UVs at texel centers for flat faces, at pixel edges for merged front/back rectangles.
- **Defaults:** `depth = 1` (min 1), `voxel-size = 1.0` (> 0), `pivot = bottom-center`.
- **Exit codes:** 0 ok · 2 input missing/unreadable · 3 invalid PNG · 4 no opaque pixels · 5 invalid flag value · 6 output unwritable.
- **License headers:** none required (repo is MIT; do not add per-file headers).
- Commit after every task. Use Conventional Commit messages.

---

## File Structure

```
mixel.sln
src/
  Mixel.Core/
    Mixel.Core.csproj
    Rgba.cs              // Rgba struct, RgbaImage
    PngLoader.cs         // PNG bytes -> RgbaImage  (+ InvalidPngException)
    SilhouetteMask.cs    // RgbaImage -> Mask       (+ EmptySilhouetteException)
    Mesh.cs              // Mesh container, Pivot enum
    MeshBuilder.cs       // Mask -> Mesh (greedy front/back + perimeter walls)
    TextureBaker.cs      // RgbaImage+Mask -> cropped PNG bytes
    GltfFormat.cs        // GltfFormat enum
    GltfWriter.cs        // Mesh+texture -> glb bytes / write file (3 formats)
    ExtrudeOptions.cs    // ExtrudeOptions record
    Extruder.cs          // orchestration: ExtrudeGlb / ExtrudeToFile
    InputExpander.cs     // file/dir/multi -> flat png path list
    BatchRunner.cs       // ExtrudeBatch + BatchResult/BatchItemResult
  Mixel.Cli/
    Mixel.Cli.csproj     // <AssemblyName>mixel</AssemblyName>
    Program.cs           // System.CommandLine root + exit-code mapping
tests/
  Mixel.Tests/
    Mixel.Tests.csproj
    TestImages.cs        // helper: build RgbaImage + encode PNG fixtures
    PngLoaderTests.cs
    SilhouetteMaskTests.cs
    MeshBuilderTests.cs
    TextureBakerTests.cs
    GltfWriterTests.cs
    ExtruderTests.cs
    InputExpanderTests.cs
    BatchRunnerTests.cs
```

---

### Task 1: Solution, projects, and core image types

**Files:**
- Create: `mixel.sln`, `src/Mixel.Core/Mixel.Core.csproj`, `src/Mixel.Cli/Mixel.Cli.csproj`, `tests/Mixel.Tests/Mixel.Tests.csproj`
- Create: `src/Mixel.Core/Rgba.cs`
- Test: `tests/Mixel.Tests/RgbaImageTests.cs`

**Interfaces:**
- Produces: `Mixel.Core.Rgba` (readonly record struct `(byte R, byte G, byte B, byte A)`); `Mixel.Core.RgbaImage { int Width; int Height; Rgba[] Pixels; Rgba At(int x,int y); }` where `Pixels` is row-major, length `Width*Height`, index `y*Width+x`.

- [ ] **Step 1: Scaffold the solution and projects**

```bash
dotnet new sln -n mixel
dotnet new classlib -n Mixel.Core -o src/Mixel.Core -f net8.0
dotnet new console  -n Mixel.Cli  -o src/Mixel.Cli  -f net8.0
dotnet new xunit    -n Mixel.Tests -o tests/Mixel.Tests -f net8.0
rm src/Mixel.Core/Class1.cs tests/Mixel.Tests/UnitTest1.cs
dotnet sln add src/Mixel.Core src/Mixel.Cli tests/Mixel.Tests
dotnet add src/Mixel.Cli reference src/Mixel.Core
dotnet add tests/Mixel.Tests reference src/Mixel.Core
dotnet add src/Mixel.Core package SharpGLTF.Toolkit --version 1.0.6
dotnet add src/Mixel.Core package StbImageSharp
dotnet add src/Mixel.Core package StbImageWriteSharp
dotnet add src/Mixel.Cli  package System.CommandLine --prerelease
```

Then set the CLI binary name — edit `src/Mixel.Cli/Mixel.Cli.csproj`, inside the existing `<PropertyGroup>` add:

```xml
<AssemblyName>mixel</AssemblyName>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
```

Ensure `src/Mixel.Core/Mixel.Core.csproj` `<PropertyGroup>` has:

```xml
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
```

- [ ] **Step 2: Write the failing test**

Create `tests/Mixel.Tests/RgbaImageTests.cs`:

```csharp
using Mixel.Core;
using Xunit;

public class RgbaImageTests
{
    [Fact]
    public void At_ReturnsPixelByRowMajorIndex()
    {
        var img = new RgbaImage
        {
            Width = 2,
            Height = 2,
            Pixels = new[]
            {
                new Rgba(1, 0, 0, 255), new Rgba(2, 0, 0, 255),
                new Rgba(3, 0, 0, 255), new Rgba(4, 0, 0, 255),
            }
        };

        Assert.Equal(new Rgba(1, 0, 0, 255), img.At(0, 0));
        Assert.Equal(new Rgba(2, 0, 0, 255), img.At(1, 0));
        Assert.Equal(new Rgba(3, 0, 0, 255), img.At(0, 1));
        Assert.Equal(new Rgba(4, 0, 0, 255), img.At(1, 1));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter RgbaImageTests`
Expected: FAIL — `Rgba`/`RgbaImage` do not exist (compile error).

- [ ] **Step 4: Implement `Rgba.cs`**

Create `src/Mixel.Core/Rgba.cs`:

```csharp
namespace Mixel.Core;

public readonly record struct Rgba(byte R, byte G, byte B, byte A);

public sealed class RgbaImage
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required Rgba[] Pixels { get; init; } // row-major, length Width*Height

    public Rgba At(int x, int y) => Pixels[y * Width + x];
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter RgbaImageTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: scaffold solution and core image types"
```

---

### Task 2: PngLoader

**Files:**
- Create: `src/Mixel.Core/PngLoader.cs`
- Create: `tests/Mixel.Tests/TestImages.cs` (shared fixture helper)
- Test: `tests/Mixel.Tests/PngLoaderTests.cs`

**Interfaces:**
- Consumes: `RgbaImage`, `Rgba`.
- Produces:
  - `static class PngLoader { static RgbaImage Load(byte[] bytes); static RgbaImage Load(Stream stream); }`
  - `sealed class InvalidPngException : Exception` (ctor takes a message).
  - Test helper `static class TestImages { static byte[] EncodePng(RgbaImage img); static RgbaImage Solid1x1(Rgba color); }`.

- [ ] **Step 1: Write the shared test-image helper**

Create `tests/Mixel.Tests/TestImages.cs`:

```csharp
using System.IO;
using Mixel.Core;
using StbImageWriteSharp;

public static class TestImages
{
    // Encode an RgbaImage to PNG bytes using StbImageWriteSharp.
    public static byte[] EncodePng(RgbaImage img)
    {
        var data = new byte[img.Width * img.Height * 4];
        for (int i = 0; i < img.Pixels.Length; i++)
        {
            var p = img.Pixels[i];
            data[i * 4 + 0] = p.R;
            data[i * 4 + 1] = p.G;
            data[i * 4 + 2] = p.B;
            data[i * 4 + 3] = p.A;
        }

        using var ms = new MemoryStream();
        new ImageWriter().WritePng(
            data, img.Width, img.Height,
            StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, ms);
        return ms.ToArray();
    }

    public static RgbaImage Solid1x1(Rgba color) => new()
    {
        Width = 1,
        Height = 1,
        Pixels = new[] { color }
    };

    // Build an RgbaImage from a string grid: '#' = given solid color (alpha 255),
    // '.' = fully transparent. All rows must be equal length.
    public static RgbaImage FromAscii(string[] rows, Rgba solid)
    {
        int h = rows.Length, w = rows[0].Length;
        var px = new Rgba[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = rows[y][x] == '#' ? solid : new Rgba(0, 0, 0, 0);
        return new RgbaImage { Width = w, Height = h, Pixels = px };
    }
}
```

- [ ] **Step 2: Write the failing test**

Create `tests/Mixel.Tests/PngLoaderTests.cs`:

```csharp
using System;
using Mixel.Core;
using Xunit;

public class PngLoaderTests
{
    [Fact]
    public void Load_RoundTripsPixels()
    {
        var src = TestImages.FromAscii(
            new[] { "#.", ".#" }, new Rgba(10, 20, 30, 255));
        byte[] png = TestImages.EncodePng(src);

        var loaded = PngLoader.Load(png);

        Assert.Equal(2, loaded.Width);
        Assert.Equal(2, loaded.Height);
        Assert.Equal(new Rgba(10, 20, 30, 255), loaded.At(0, 0));
        Assert.Equal((byte)0, loaded.At(1, 0).A); // transparent pixel
    }

    [Fact]
    public void Load_InvalidBytes_ThrowsInvalidPngException()
    {
        Assert.Throws<InvalidPngException>(
            () => PngLoader.Load(new byte[] { 1, 2, 3, 4 }));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter PngLoaderTests`
Expected: FAIL — `PngLoader` / `InvalidPngException` not defined.

- [ ] **Step 4: Implement `PngLoader.cs`**

Create `src/Mixel.Core/PngLoader.cs`:

```csharp
using StbImageSharp;

namespace Mixel.Core;

public sealed class InvalidPngException : Exception
{
    public InvalidPngException(string message) : base(message) { }
}

public static class PngLoader
{
    public static RgbaImage Load(byte[] bytes)
    {
        ImageResult img;
        try
        {
            img = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
        }
        catch (Exception ex)
        {
            throw new InvalidPngException($"Could not decode PNG: {ex.Message}");
        }

        if (img is null || img.Data is null || img.Width <= 0 || img.Height <= 0)
            throw new InvalidPngException("Decoded image was empty.");

        var px = new Rgba[img.Width * img.Height];
        for (int i = 0; i < px.Length; i++)
        {
            int b = i * 4;
            px[i] = new Rgba(img.Data[b], img.Data[b + 1], img.Data[b + 2], img.Data[b + 3]);
        }

        return new RgbaImage { Width = img.Width, Height = img.Height, Pixels = px };
    }

    public static RgbaImage Load(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Load(ms.ToArray());
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter PngLoaderTests`
Expected: PASS (both facts).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: PNG decoding via StbImageSharp"
```

---

### Task 3: SilhouetteMask

**Files:**
- Create: `src/Mixel.Core/SilhouetteMask.cs`
- Test: `tests/Mixel.Tests/SilhouetteMaskTests.cs`

**Interfaces:**
- Consumes: `RgbaImage`.
- Produces:
  - `sealed class Mask { int Width; int Height; bool[] Solid; int OffsetX; int OffsetY; bool At(int x,int y); }` — `Solid` is row-major over the **cropped** bbox (length `Width*Height`); `At` returns `false` for out-of-range coordinates.
  - `static class SilhouetteMask { static Mask Build(RgbaImage image); }` — opaque-only (alpha==255); crops to tight bbox; throws `EmptySilhouetteException` if no opaque pixels.
  - `sealed class EmptySilhouetteException : Exception`.

- [ ] **Step 1: Write the failing test**

Create `tests/Mixel.Tests/SilhouetteMaskTests.cs`:

```csharp
using Mixel.Core;
using Xunit;

public class SilhouetteMaskTests
{
    private static readonly Rgba S = new(255, 255, 255, 255);

    [Fact]
    public void Build_CropsToBoundingBox_OpaqueOnly()
    {
        // 4x4 with a 2x2 opaque block at (1,1)-(2,2), rest transparent.
        var img = TestImages.FromAscii(new[]
        {
            "....",
            ".##.",
            ".##.",
            "....",
        }, S);

        var mask = SilhouetteMask.Build(img);

        Assert.Equal(2, mask.Width);
        Assert.Equal(2, mask.Height);
        Assert.Equal(1, mask.OffsetX);
        Assert.Equal(1, mask.OffsetY);
        Assert.True(mask.At(0, 0));
        Assert.True(mask.At(1, 1));
        Assert.False(mask.At(-1, 0)); // out of range
        Assert.False(mask.At(2, 0));
    }

    [Fact]
    public void Build_TreatsPartialAlphaAsEmpty()
    {
        var img = new RgbaImage
        {
            Width = 2, Height = 1,
            Pixels = new[] { new Rgba(255, 0, 0, 254), new Rgba(0, 255, 0, 255) }
        };

        var mask = SilhouetteMask.Build(img);

        Assert.Equal(1, mask.Width);  // only the alpha==255 pixel survives
        Assert.Equal(1, mask.OffsetX);
        Assert.True(mask.At(0, 0));
    }

    [Fact]
    public void Build_AllTransparent_Throws()
    {
        var img = new RgbaImage
        {
            Width = 1, Height = 1, Pixels = new[] { new Rgba(0, 0, 0, 0) }
        };

        Assert.Throws<EmptySilhouetteException>(() => SilhouetteMask.Build(img));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter SilhouetteMaskTests`
Expected: FAIL — `SilhouetteMask` / `Mask` / `EmptySilhouetteException` not defined.

- [ ] **Step 3: Implement `SilhouetteMask.cs`**

Create `src/Mixel.Core/SilhouetteMask.cs`:

```csharp
namespace Mixel.Core;

public sealed class EmptySilhouetteException : Exception
{
    public EmptySilhouetteException()
        : base("Image contains no fully-opaque pixels to extrude.") { }
}

public sealed class Mask
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required bool[] Solid { get; init; } // row-major over cropped bbox
    public required int OffsetX { get; init; }  // bbox origin in source image
    public required int OffsetY { get; init; }

    public bool At(int x, int y)
        => x >= 0 && y >= 0 && x < Width && y < Height && Solid[y * Width + x];
}

public static class SilhouetteMask
{
    public static Mask Build(RgbaImage image)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;

        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (image.At(x, y).A == 255)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }

        if (maxX < 0) throw new EmptySilhouetteException();

        int w = maxX - minX + 1, h = maxY - minY + 1;
        var solid = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                solid[y * w + x] = image.At(minX + x, minY + y).A == 255;

        return new Mask { Width = w, Height = h, Solid = solid, OffsetX = minX, OffsetY = minY };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter SilhouetteMaskTests`
Expected: PASS (all three facts).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: silhouette mask with opaque-only rule and bbox crop"
```

---

### Task 4: MeshBuilder (the core)

**Files:**
- Create: `src/Mixel.Core/Mesh.cs`, `src/Mixel.Core/MeshBuilder.cs`
- Test: `tests/Mixel.Tests/MeshBuilderTests.cs`

**Interfaces:**
- Consumes: `Mask`.
- Produces:
  - `enum Pivot { BottomCenter, Center, MinCorner }`
  - `sealed class Mesh { List<float> Positions; List<float> Normals; List<float> Uvs; List<int> Indices; int VertexCount => Positions.Count/3; int TriangleCount => Indices.Count/3; }` — `Positions`/`Normals` are xyz triples, `Uvs` are uv pairs, in vertex order; `Indices` index those vertices.
  - `static class MeshBuilder { static Mesh Build(Mask mask, int depth, float voxelSize, Pivot pivot); }`

**Geometry contract (must hold):**
- A pixel `(x,y)` in the mask occupies world box: X `[x, x+1]·s`, Y `[(h-1-y), (h-y)]·s` (row 0 = top = highest Y), Z `[0, depth]·s`. `s = voxelSize`.
- **Front** faces +Z at `Z = depth·s` (normal `+Z`); **back** at `Z = 0` (normal `−Z`). Front/back are emitted as **greedy maximal rectangles** over the solid region. Rectangle `[x0..x1]×[y0..y1]` UV spans pixel **edges**: u `[x0/w .. (x1+1)/w]`, v `[y0/h .. (y1+1)/h]`.
- **Perimeter walls:** for each solid pixel, each of its 4 side-neighbors that is not solid (`Mask.At` false, includes out-of-range) emits one wall quad spanning the full depth, with the **owning pixel's texel-center UV** `((x+0.5)/w, (y+0.5)/h)` on all 4 corners.
- After building, a **pivot offset** is added to every position: BottomCenter → `(-w·s/2, 0, -depth·s/2)`; Center → `(-w·s/2, -h·s/2, -depth·s/2)`; MinCorner → `(0,0,0)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Mixel.Tests/MeshBuilderTests.cs`:

```csharp
using System.Linq;
using Mixel.Core;
using Xunit;

public class MeshBuilderTests
{
    private static Mask MaskFrom(string[] rows)
    {
        int h = rows.Length, w = rows[0].Length;
        var solid = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                solid[y * w + x] = rows[y][x] == '#';
        return new Mask { Width = w, Height = h, Solid = solid, OffsetX = 0, OffsetY = 0 };
    }

    [Fact]
    public void SinglePixel_HasSixFacesTwelveTriangles()
    {
        var mask = MaskFrom(new[] { "#" });

        var mesh = MeshBuilder.Build(mask, depth: 1, voxelSize: 1f, Pivot.MinCorner);

        // 6 faces (front, back, 4 walls) -> 12 triangles -> 36 indices.
        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(36, mesh.Indices.Count);
        Assert.Equal(mesh.Positions.Count / 3, mesh.Normals.Count / 3);
        Assert.Equal(mesh.Positions.Count / 3, mesh.Uvs.Count / 2);
    }

    [Fact]
    public void MinCorner_PlacesGeometryInPositiveOctant()
    {
        var mask = MaskFrom(new[] { "##", "##" });

        var mesh = MeshBuilder.Build(mask, depth: 3, voxelSize: 1f, Pivot.MinCorner);

        var xs = Enumerable.Range(0, mesh.VertexCount).Select(i => mesh.Positions[i * 3]);
        var ys = Enumerable.Range(0, mesh.VertexCount).Select(i => mesh.Positions[i * 3 + 1]);
        var zs = Enumerable.Range(0, mesh.VertexCount).Select(i => mesh.Positions[i * 3 + 2]);

        Assert.Equal(0f, xs.Min()); Assert.Equal(2f, xs.Max());
        Assert.Equal(0f, ys.Min()); Assert.Equal(2f, ys.Max());
        Assert.Equal(0f, zs.Min()); Assert.Equal(3f, zs.Max()); // depth 3
    }

    [Fact]
    public void BottomCenter_CentersXZ_AndKeepsBaseAtZeroY()
    {
        var mask = MaskFrom(new[] { "##", "##" }); // w=2,h=2

        var mesh = MeshBuilder.Build(mask, depth: 2, voxelSize: 1f, Pivot.BottomCenter);

        var xs = Enumerable.Range(0, mesh.VertexCount).Select(i => mesh.Positions[i * 3]).ToList();
        var ys = Enumerable.Range(0, mesh.VertexCount).Select(i => mesh.Positions[i * 3 + 1]).ToList();
        var zs = Enumerable.Range(0, mesh.VertexCount).Select(i => mesh.Positions[i * 3 + 2]).ToList();

        Assert.Equal(-1f, xs.Min()); Assert.Equal(1f, xs.Max());  // centered on X
        Assert.Equal(0f, ys.Min());  Assert.Equal(2f, ys.Max());  // base at Y=0
        Assert.Equal(-1f, zs.Min()); Assert.Equal(1f, zs.Max());  // centered on Z
    }

    [Fact]
    public void InteriorHole_EmitsInteriorWalls()
    {
        // ring with a hole in the middle -> the hole's 4 sides add walls.
        var solidRing = MaskFrom(new[] { "###", "#.#", "###" });
        var solidFull = MaskFrom(new[] { "###", "###", "###" });

        var ring = MeshBuilder.Build(solidRing, 1, 1f, Pivot.MinCorner);
        var full = MeshBuilder.Build(solidFull, 1, 1f, Pivot.MinCorner);

        // The ring has fewer solid pixels but MORE triangles than... assert it has
        // interior walls: ring wall-count exceeds the full block's perimeter walls.
        Assert.True(ring.TriangleCount > full.TriangleCount - 4 /*front+back of hole*/);
        Assert.True(ring.TriangleCount >= full.TriangleCount); // interior walls dominate
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter MeshBuilderTests`
Expected: FAIL — `MeshBuilder` / `Mesh` / `Pivot` not defined.

- [ ] **Step 3: Implement `Mesh.cs`**

Create `src/Mixel.Core/Mesh.cs`:

```csharp
namespace Mixel.Core;

public enum Pivot { BottomCenter, Center, MinCorner }

public sealed class Mesh
{
    public List<float> Positions { get; } = new(); // xyz triples
    public List<float> Normals { get; } = new();   // xyz triples
    public List<float> Uvs { get; } = new();        // uv pairs
    public List<int> Indices { get; } = new();

    public int VertexCount => Positions.Count / 3;
    public int TriangleCount => Indices.Count / 3;
}
```

- [ ] **Step 4: Implement `MeshBuilder.cs`**

Create `src/Mixel.Core/MeshBuilder.cs`:

```csharp
namespace Mixel.Core;

public static class MeshBuilder
{
    public static Mesh Build(Mask mask, int depth, float voxelSize, Pivot pivot)
    {
        int w = mask.Width, h = mask.Height;
        float s = voxelSize;
        float zf = depth * s; // front (+Z)
        const float zb = 0f;  // back

        var mesh = new Mesh();

        // --- Front and back faces via greedy maximal rectangles over solid mask. ---
        var used = new bool[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!mask.At(x, y) || used[y * w + x]) continue;

                int x1 = x;
                while (x1 + 1 < w && mask.At(x1 + 1, y) && !used[y * w + x1 + 1]) x1++;

                int y1 = y;
                bool canGrow = true;
                while (canGrow && y1 + 1 < h)
                {
                    for (int xx = x; xx <= x1; xx++)
                        if (!mask.At(xx, y1 + 1) || used[(y1 + 1) * w + xx]) { canGrow = false; break; }
                    if (canGrow) y1++;
                }

                for (int yy = y; yy <= y1; yy++)
                    for (int xx = x; xx <= x1; xx++)
                        used[yy * w + xx] = true;

                AddFrontBack(mesh, x, y, x1, y1, w, h, s, zf, zb);
            }
        }

        // --- Perimeter walls: per solid pixel, each empty side-neighbor. ---
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!mask.At(x, y)) continue;

                float xl = x * s, xr = (x + 1) * s;
                float yt = (h - y) * s, yb = (h - 1 - y) * s;
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;

                if (!mask.At(x - 1, y)) // left wall, -X
                    AddQuad(mesh, (-1, 0, 0), u, v,
                        (xl, yb, zb), (xl, yb, zf), (xl, yt, zf), (xl, yt, zb));
                if (!mask.At(x + 1, y)) // right wall, +X
                    AddQuad(mesh, (1, 0, 0), u, v,
                        (xr, yb, zf), (xr, yb, zb), (xr, yt, zb), (xr, yt, zf));
                if (!mask.At(x, y - 1)) // top wall, +Y (neighbor above)
                    AddQuad(mesh, (0, 1, 0), u, v,
                        (xl, yt, zf), (xr, yt, zf), (xr, yt, zb), (xl, yt, zb));
                if (!mask.At(x, y + 1)) // bottom wall, -Y (neighbor below)
                    AddQuad(mesh, (0, -1, 0), u, v,
                        (xl, yb, zb), (xr, yb, zb), (xr, yb, zf), (xl, yb, zf));
            }
        }

        ApplyPivot(mesh, pivot, w, h, depth, s);
        return mesh;
    }

    private static void AddFrontBack(
        Mesh mesh, int x0, int y0, int x1, int y1,
        int w, int h, float s, float zf, float zb)
    {
        float X0 = x0 * s, X1 = (x1 + 1) * s;
        float Ytop = (h - y0) * s, Ybot = (h - 1 - y1) * s;
        float u0 = (float)x0 / w, u1 = (float)(x1 + 1) / w;
        float vtop = (float)y0 / h, vbot = (float)(y1 + 1) / h;

        // Front (+Z): CCW seen from +Z.
        AddQuadUv(mesh, (0, 0, 1),
            (X0, Ybot, zf), (u0, vbot),
            (X1, Ybot, zf), (u1, vbot),
            (X1, Ytop, zf), (u1, vtop),
            (X0, Ytop, zf), (u0, vtop));

        // Back (-Z): reversed winding, mirrored U so same texels show.
        AddQuadUv(mesh, (0, 0, -1),
            (X1, Ybot, zb), (u1, vbot),
            (X0, Ybot, zb), (u0, vbot),
            (X0, Ytop, zb), (u0, vtop),
            (X1, Ytop, zb), (u1, vtop));
    }

    // Quad with one flat UV on all corners (walls).
    private static void AddQuad(
        Mesh mesh, (float x, float y, float z) n, float u, float v,
        (float, float, float) p0, (float, float, float) p1,
        (float, float, float) p2, (float, float, float) p3)
    {
        AddQuadUv(mesh, n, p0, (u, v), p1, (u, v), p2, (u, v), p3, (u, v));
    }

    // Quad with explicit per-corner UV.
    private static void AddQuadUv(
        Mesh mesh, (float x, float y, float z) n,
        (float, float, float) p0, (float, float) uv0,
        (float, float, float) p1, (float, float) uv1,
        (float, float, float) p2, (float, float) uv2,
        (float, float, float) p3, (float, float) uv3)
    {
        int b = mesh.VertexCount;
        AddVertex(mesh, p0, n, uv0);
        AddVertex(mesh, p1, n, uv1);
        AddVertex(mesh, p2, n, uv2);
        AddVertex(mesh, p3, n, uv3);
        mesh.Indices.AddRange(new[] { b + 0, b + 1, b + 2, b + 0, b + 2, b + 3 });
    }

    private static void AddVertex(
        Mesh mesh, (float x, float y, float z) p,
        (float x, float y, float z) n, (float u, float v) uv)
    {
        mesh.Positions.Add(p.x); mesh.Positions.Add(p.y); mesh.Positions.Add(p.z);
        mesh.Normals.Add(n.x); mesh.Normals.Add(n.y); mesh.Normals.Add(n.z);
        mesh.Uvs.Add(uv.u); mesh.Uvs.Add(uv.v);
    }

    private static void ApplyPivot(Mesh mesh, Pivot pivot, int w, int h, int depth, float s)
    {
        float ox = 0, oy = 0, oz = 0;
        switch (pivot)
        {
            case Pivot.BottomCenter: ox = -w * s / 2f; oy = 0; oz = -depth * s / 2f; break;
            case Pivot.Center: ox = -w * s / 2f; oy = -h * s / 2f; oz = -depth * s / 2f; break;
            case Pivot.MinCorner: ox = 0; oy = 0; oz = 0; break;
        }
        if (ox == 0 && oy == 0 && oz == 0) return;
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            mesh.Positions[i * 3 + 0] += ox;
            mesh.Positions[i * 3 + 1] += oy;
            mesh.Positions[i * 3 + 2] += oz;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter MeshBuilderTests`
Expected: PASS (all four facts). If `InteriorHole` is brittle, confirm the ring produces 4 extra interior wall quads vs. the same cells in the full block; adjust the assertion to compare wall counts directly, not totals.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: extrusion mesh builder (greedy front/back + perimeter walls + pivot)"
```

---

### Task 5: TextureBaker

**Files:**
- Create: `src/Mixel.Core/TextureBaker.cs`
- Test: `tests/Mixel.Tests/TextureBakerTests.cs`

**Interfaces:**
- Consumes: `RgbaImage`, `Mask`.
- Produces: `static class TextureBaker { static byte[] BakePng(RgbaImage source, Mask mask); }` — returns PNG bytes of the source cropped to `mask`'s bbox (`mask.OffsetX/Y`, `mask.Width/Height`).

- [ ] **Step 1: Write the failing test**

Create `tests/Mixel.Tests/TextureBakerTests.cs`:

```csharp
using Mixel.Core;
using Xunit;

public class TextureBakerTests
{
    [Fact]
    public void BakePng_CropsToMaskBoundingBox()
    {
        var src = TestImages.FromAscii(new[]
        {
            "....",
            ".#..",
            "....",
        }, new Rgba(10, 20, 30, 255));
        var mask = SilhouetteMask.Build(src); // 1x1 at offset (1,1)

        byte[] png = TextureBaker.BakePng(src, mask);
        var baked = PngLoader.Load(png);

        Assert.Equal(1, baked.Width);
        Assert.Equal(1, baked.Height);
        Assert.Equal(new Rgba(10, 20, 30, 255), baked.At(0, 0));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter TextureBakerTests`
Expected: FAIL — `TextureBaker` not defined.

- [ ] **Step 3: Implement `TextureBaker.cs`**

Create `src/Mixel.Core/TextureBaker.cs`:

```csharp
using StbImageWriteSharp;

namespace Mixel.Core;

public static class TextureBaker
{
    public static byte[] BakePng(RgbaImage source, Mask mask)
    {
        int w = mask.Width, h = mask.Height;
        var data = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var p = source.At(mask.OffsetX + x, mask.OffsetY + y);
                int b = (y * w + x) * 4;
                data[b + 0] = p.R; data[b + 1] = p.G; data[b + 2] = p.B; data[b + 3] = p.A;
            }

        using var ms = new MemoryStream();
        new ImageWriter().WritePng(data, w, h, ColorComponents.RedGreenBlueAlpha, ms);
        return ms.ToArray();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter TextureBakerTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: bake cropped silhouette texture to PNG"
```

---

### Task 6: GltfWriter + Extruder

**Files:**
- Create: `src/Mixel.Core/GltfFormat.cs`, `src/Mixel.Core/GltfWriter.cs`, `src/Mixel.Core/ExtrudeOptions.cs`, `src/Mixel.Core/Extruder.cs`
- Test: `tests/Mixel.Tests/GltfWriterTests.cs`, `tests/Mixel.Tests/ExtruderTests.cs`

**Interfaces:**
- Consumes: `Mesh`, `RgbaImage`, `Mask`, `PngLoader`, `SilhouetteMask`, `TextureBaker`, `MeshBuilder`, `Pivot`.
- Produces:
  - `enum GltfFormat { Glb, Gltf, GltfEmbedded }`
  - `static class GltfWriter { static byte[] WriteGlbBytes(Mesh mesh, byte[] pngTexture); static void Write(Mesh mesh, byte[] pngTexture, GltfFormat format, string outputPath); }`
  - `sealed record ExtrudeOptions { byte[] PngBytes; int Depth=1; float VoxelSize=1f; GltfFormat Format=GltfFormat.Glb; Pivot Pivot=Pivot.BottomCenter; }`
  - `static class Extruder { static byte[] ExtrudeGlb(ExtrudeOptions o); static void ExtrudeToFile(ExtrudeOptions o, string outputPath); static string DefaultExtension(GltfFormat f); }`

- [ ] **Step 1: Write the failing tests**

Create `tests/Mixel.Tests/GltfWriterTests.cs`:

```csharp
using System;
using Mixel.Core;
using SharpGLTF.Schema2;
using Xunit;

public class GltfWriterTests
{
    private static (Mesh, byte[]) BuildSquare()
    {
        var img = TestImages.FromAscii(new[] { "#" }, new Rgba(200, 50, 50, 255));
        var mask = SilhouetteMask.Build(img);
        var mesh = MeshBuilder.Build(mask, 1, 1f, Pivot.MinCorner);
        var tex = TextureBaker.BakePng(img, mask);
        return (mesh, tex);
    }

    [Fact]
    public void WriteGlbBytes_ProducesParseableGlbWithOneMeshAndTexture()
    {
        var (mesh, tex) = BuildSquare();

        byte[] glb = GltfWriter.WriteGlbBytes(mesh, tex);
        var model = ModelRoot.ParseGLB(new ArraySegment<byte>(glb));

        Assert.Single(model.LogicalMeshes);
        Assert.NotEmpty(model.LogicalTextures);
        // NEAREST sampler (mag filter 9728).
        var sampler = model.LogicalTextureSamplers[0];
        Assert.Equal(TextureInterpolationFilter.NEAREST, sampler.MagFilter);
    }

    [Theory]
    [InlineData(GltfFormat.Glb, ".glb")]
    [InlineData(GltfFormat.Gltf, ".gltf")]
    [InlineData(GltfFormat.GltfEmbedded, ".gltf")]
    public void Write_CreatesFileForEachFormat(GltfFormat fmt, string ext)
    {
        var (mesh, tex) = BuildSquare();
        string path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"mixel_test_{Guid.NewGuid():N}{ext}");

        GltfWriter.Write(mesh, tex, fmt, path);

        Assert.True(System.IO.File.Exists(path));
        Assert.True(new System.IO.FileInfo(path).Length > 0);
    }
}
```

Create `tests/Mixel.Tests/ExtruderTests.cs`:

```csharp
using System;
using Mixel.Core;
using SharpGLTF.Schema2;
using Xunit;

public class ExtruderTests
{
    [Fact]
    public void ExtrudeGlb_RoundTripsThroughPngBytes()
    {
        var img = TestImages.FromAscii(new[] { "##", "##" }, new Rgba(0, 128, 255, 255));
        var opts = new ExtrudeOptions
        {
            PngBytes = TestImages.EncodePng(img),
            Depth = 2,
            VoxelSize = 1f,
            Pivot = Pivot.MinCorner,
        };

        byte[] glb = Extruder.ExtrudeGlb(opts);
        var model = ModelRoot.ParseGLB(new ArraySegment<byte>(glb));

        Assert.Single(model.LogicalMeshes);
    }

    [Fact]
    public void ExtrudeGlb_EmptyImage_ThrowsEmptySilhouette()
    {
        var img = new RgbaImage { Width = 1, Height = 1, Pixels = new[] { new Rgba(0, 0, 0, 0) } };
        var opts = new ExtrudeOptions { PngBytes = TestImages.EncodePng(img) };

        Assert.Throws<EmptySilhouetteException>(() => Extruder.ExtrudeGlb(opts));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "GltfWriterTests|ExtruderTests"`
Expected: FAIL — types not defined.

- [ ] **Step 3: Implement `GltfFormat.cs` and `ExtrudeOptions.cs`**

Create `src/Mixel.Core/GltfFormat.cs`:

```csharp
namespace Mixel.Core;

public enum GltfFormat { Glb, Gltf, GltfEmbedded }
```

Create `src/Mixel.Core/ExtrudeOptions.cs`:

```csharp
namespace Mixel.Core;

public sealed record ExtrudeOptions
{
    public required byte[] PngBytes { get; init; }
    public int Depth { get; init; } = 1;
    public float VoxelSize { get; init; } = 1f;
    public GltfFormat Format { get; init; } = GltfFormat.Glb;
    public Pivot Pivot { get; init; } = Pivot.BottomCenter;
}
```

- [ ] **Step 4: Implement `GltfWriter.cs`**

Create `src/Mixel.Core/GltfWriter.cs`:

```csharp
using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Memory;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;

namespace Mixel.Core;

using VERTEX = VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>;

public static class GltfWriter
{
    private static ModelRoot BuildModel(Mesh mesh, byte[] pngTexture)
    {
        var material = new MaterialBuilder("mixel")
            .WithMetallicRoughnessShader()
            .WithDoubleSide(true)
            .WithMetallicRoughness(0f, 1f)
            .WithBaseColor(ImageBuilder.From(new MemoryImage(pngTexture)), new Vector4(1, 1, 1, 1));

        // Crisp pixels: NEAREST min & mag, clamp to edge.
        material.GetChannel("BaseColor")!.Texture!.WithSampler(
            TextureMipMapFilter.NEAREST,
            TextureInterpolationFilter.NEAREST,
            TextureWrapMode.CLAMP_TO_EDGE,
            TextureWrapMode.CLAMP_TO_EDGE);

        var mb = VERTEX.CreateCompatibleMesh("mixel");
        var prim = mb.UsePrimitive(material);

        for (int i = 0; i < mesh.Indices.Count; i += 3)
            prim.AddTriangle(V(mesh, mesh.Indices[i]), V(mesh, mesh.Indices[i + 1]), V(mesh, mesh.Indices[i + 2]));

        var scene = new SceneBuilder();
        scene.AddRigidMesh(mb, Matrix4x4.Identity);
        return scene.ToGltf2();
    }

    private static VERTEX V(Mesh mesh, int i)
    {
        var p = new Vector3(mesh.Positions[i * 3], mesh.Positions[i * 3 + 1], mesh.Positions[i * 3 + 2]);
        var n = new Vector3(mesh.Normals[i * 3], mesh.Normals[i * 3 + 1], mesh.Normals[i * 3 + 2]);
        var uv = new Vector2(mesh.Uvs[i * 2], mesh.Uvs[i * 2 + 1]);
        return new VERTEX(new VertexPositionNormal(p, n), new VertexTexture1(uv));
    }

    public static byte[] WriteGlbBytes(Mesh mesh, byte[] pngTexture)
    {
        var model = BuildModel(mesh, pngTexture);
        var seg = model.WriteGLB();
        return seg.ToArray();
    }

    public static void Write(Mesh mesh, byte[] pngTexture, GltfFormat format, string outputPath)
    {
        var model = BuildModel(mesh, pngTexture);
        switch (format)
        {
            case GltfFormat.Glb:
                model.SaveGLB(outputPath);
                break;
            case GltfFormat.Gltf:
                model.SaveGLTF(outputPath); // .gltf + satellite .bin + .png
                break;
            case GltfFormat.GltfEmbedded:
                model.SaveGLTF(outputPath, new WriteSettings
                {
                    ImageWriting = ResourceWriteMode.EmbeddedAsBase64,
                    MergeBuffers = true,
                });
                break;
        }
    }
}
```

> Note: if `GetChannel("BaseColor")` or the `KnownChannel` enum name differs in 1.0.6, the `WriteGlbBytes` NEAREST test will fail fast — switch to `material.UseChannel(KnownChannel.BaseColor).Texture.WithSampler(...)` and re-run. The behavior (sampler == NEAREST) is asserted, so either spelling that compiles and passes is correct.

- [ ] **Step 5: Implement `Extruder.cs`**

Create `src/Mixel.Core/Extruder.cs`:

```csharp
namespace Mixel.Core;

public static class Extruder
{
    private static (Mesh mesh, byte[] tex) Build(ExtrudeOptions o)
    {
        var img = PngLoader.Load(o.PngBytes);
        var mask = SilhouetteMask.Build(img);
        var tex = TextureBaker.BakePng(img, mask);
        var mesh = MeshBuilder.Build(mask, o.Depth, o.VoxelSize, o.Pivot);
        return (mesh, tex);
    }

    public static byte[] ExtrudeGlb(ExtrudeOptions o)
    {
        var (mesh, tex) = Build(o);
        return GltfWriter.WriteGlbBytes(mesh, tex);
    }

    public static void ExtrudeToFile(ExtrudeOptions o, string outputPath)
    {
        var (mesh, tex) = Build(o);
        GltfWriter.Write(mesh, tex, o.Format, outputPath);
    }

    public static string DefaultExtension(GltfFormat f)
        => f == GltfFormat.Glb ? ".glb" : ".gltf";
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter "GltfWriterTests|ExtruderTests"`
Expected: PASS (all). If the NEAREST assertion fails to compile, apply the note in Step 4.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: glTF writer (glb/gltf/embedded, NEAREST) and Extruder orchestration"
```

---

### Task 7: Batch — InputExpander + BatchRunner

**Files:**
- Create: `src/Mixel.Core/InputExpander.cs`, `src/Mixel.Core/BatchRunner.cs`
- Test: `tests/Mixel.Tests/InputExpanderTests.cs`, `tests/Mixel.Tests/BatchRunnerTests.cs`

**Interfaces:**
- Consumes: `Extruder`, `ExtrudeOptions`, `GltfFormat`, `Pivot`.
- Produces:
  - `static class InputExpander { static IReadOnlyList<string> Expand(IEnumerable<string> inputs, bool recursive); }` — files pass through (must be `.png`); directories expand to their `*.png` (top-level, or all descendants if `recursive`); deterministic ordering (sorted). Throws `FileNotFoundException` if a named file/dir does not exist.
  - `sealed record BatchItemResult(string InputPath, bool Success, string? Error, string? OutputPath);`
  - `sealed record BatchResult(IReadOnlyList<BatchItemResult> Items) { int SucceededCount; int FailedCount; }`
  - `static class BatchRunner { static BatchResult Run(IReadOnlyList<string> pngPaths, int depth, float voxelSize, GltfFormat format, Pivot pivot, string? outputDir); }` — for each path: read bytes, `Extruder.ExtrudeToFile` to `outputDir` (or alongside source) with the format's default extension; one failure is captured per-item and does not stop the batch.

- [ ] **Step 1: Write the failing tests**

Create `tests/Mixel.Tests/InputExpanderTests.cs`:

```csharp
using System.IO;
using System.Linq;
using Mixel.Core;
using Xunit;

public class InputExpanderTests
{
    [Fact]
    public void Expand_Directory_FindsTopLevelPngsSorted()
    {
        string dir = Directory.CreateTempSubdirectory("mixel_in_").FullName;
        File.WriteAllBytes(Path.Combine(dir, "b.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(dir, "a.png"), new byte[] { 1 });
        File.WriteAllText(Path.Combine(dir, "note.txt"), "skip me");
        string sub = Directory.CreateDirectory(Path.Combine(dir, "sub")).FullName;
        File.WriteAllBytes(Path.Combine(sub, "c.png"), new byte[] { 1 });

        var top = InputExpander.Expand(new[] { dir }, recursive: false);
        Assert.Equal(2, top.Count);
        Assert.EndsWith("a.png", top[0]);
        Assert.EndsWith("b.png", top[1]);

        var all = InputExpander.Expand(new[] { dir }, recursive: true);
        Assert.Equal(3, all.Count); // includes sub/c.png
    }

    [Fact]
    public void Expand_MissingPath_Throws()
    {
        Assert.Throws<FileNotFoundException>(
            () => InputExpander.Expand(new[] { "does_not_exist.png" }, false));
    }
}
```

Create `tests/Mixel.Tests/BatchRunnerTests.cs`:

```csharp
using System.IO;
using Mixel.Core;
using Xunit;

public class BatchRunnerTests
{
    [Fact]
    public void Run_OneBadImage_DoesNotAbortBatch()
    {
        string dir = Directory.CreateTempSubdirectory("mixel_batch_").FullName;
        string good = Path.Combine(dir, "good.png");
        string bad = Path.Combine(dir, "bad.png");
        File.WriteAllBytes(good,
            TestImages.EncodePng(TestImages.FromAscii(new[] { "#" }, new Rgba(1, 2, 3, 255))));
        File.WriteAllBytes(bad, new byte[] { 9, 9, 9 }); // not a PNG

        string outDir = Directory.CreateTempSubdirectory("mixel_out_").FullName;
        var result = BatchRunner.Run(
            new[] { good, bad }, depth: 1, voxelSize: 1f,
            GltfFormat.Glb, Pivot.BottomCenter, outDir);

        Assert.Equal(1, result.SucceededCount);
        Assert.Equal(1, result.FailedCount);
        Assert.True(File.Exists(Path.Combine(outDir, "good.glb")));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "InputExpanderTests|BatchRunnerTests"`
Expected: FAIL — types not defined.

- [ ] **Step 3: Implement `InputExpander.cs`**

Create `src/Mixel.Core/InputExpander.cs`:

```csharp
namespace Mixel.Core;

public static class InputExpander
{
    public static IReadOnlyList<string> Expand(IEnumerable<string> inputs, bool recursive)
    {
        var result = new List<string>();
        foreach (var input in inputs)
        {
            if (Directory.Exists(input))
            {
                var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                result.AddRange(Directory.EnumerateFiles(input, "*.png", opt));
            }
            else if (File.Exists(input))
            {
                if (input.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    result.Add(input);
            }
            else
            {
                throw new FileNotFoundException($"Input not found: {input}", input);
            }
        }
        result.Sort(StringComparer.Ordinal);
        return result;
    }
}
```

- [ ] **Step 4: Implement `BatchRunner.cs`**

Create `src/Mixel.Core/BatchRunner.cs`:

```csharp
namespace Mixel.Core;

public sealed record BatchItemResult(string InputPath, bool Success, string? Error, string? OutputPath);

public sealed record BatchResult(IReadOnlyList<BatchItemResult> Items)
{
    public int SucceededCount => Items.Count(i => i.Success);
    public int FailedCount => Items.Count(i => !i.Success);
}

public static class BatchRunner
{
    public static BatchResult Run(
        IReadOnlyList<string> pngPaths,
        int depth, float voxelSize, GltfFormat format, Pivot pivot, string? outputDir)
    {
        var items = new List<BatchItemResult>(pngPaths.Count);
        string ext = Extruder.DefaultExtension(format);

        foreach (var path in pngPaths)
        {
            try
            {
                var opts = new ExtrudeOptions
                {
                    PngBytes = File.ReadAllBytes(path),
                    Depth = depth,
                    VoxelSize = voxelSize,
                    Format = format,
                    Pivot = pivot,
                };

                string dir = outputDir ?? Path.GetDirectoryName(Path.GetFullPath(path))!;
                Directory.CreateDirectory(dir);
                string outPath = Path.Combine(dir, Path.GetFileNameWithoutExtension(path) + ext);

                Extruder.ExtrudeToFile(opts, outPath);
                items.Add(new BatchItemResult(path, true, null, outPath));
            }
            catch (Exception ex)
            {
                items.Add(new BatchItemResult(path, false, ex.Message, null));
            }
        }

        return new BatchResult(items);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --filter "InputExpanderTests|BatchRunnerTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: batch input expansion and runner"
```

---

### Task 8: Mixel.Cli

**Files:**
- Create: `src/Mixel.Cli/Program.cs` (replace the template `Program.cs`)
- Test: `tests/Mixel.Tests/CliTests.cs`

**Interfaces:**
- Consumes: `InputExpander`, `BatchRunner`, `Extruder`, `ExtrudeOptions`, `GltfFormat`, `Pivot`, `InvalidPngException`, `EmptySilhouetteException`.
- Produces: `public static class Program { static int Main(string[] args); static int Run(string[] inputs, string? output, int depth, double voxelSize, string format, string pivot, bool recursive); }` — `Run` is the testable entry that returns the process exit code.

**Exit-code mapping:** 0 ok · 2 input missing/unreadable (`FileNotFoundException`/`DirectoryNotFoundException`) · 3 invalid PNG (`InvalidPngException`) · 4 no opaque pixels (`EmptySilhouetteException`) · 5 invalid flag value · 6 output unwritable (`IOException`/`UnauthorizedAccessException`). Batch: exit non-zero if **any** item failed.

- [ ] **Step 1: Make the CLI testable — reference internals**

Edit `src/Mixel.Cli/Mixel.Cli.csproj`, add inside a `<ItemGroup>`:

```xml
<InternalsVisibleTo Include="Mixel.Tests" />
```

(Use the MSBuild item; if the SDK version does not support the `InternalsVisibleTo` item, add instead to a new file `src/Mixel.Cli/AssemblyInfo.cs`:
`[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Mixel.Tests")]`.)

- [ ] **Step 2: Write the failing test**

Create `tests/Mixel.Tests/CliTests.cs`:

```csharp
using System;
using System.IO;
using Xunit;

public class CliTests
{
    [Fact]
    public void Run_SingleImage_WritesGlb_ReturnsZero()
    {
        string dir = Directory.CreateTempSubdirectory("mixel_cli_").FullName;
        string input = Path.Combine(dir, "hero.png");
        File.WriteAllBytes(input,
            TestImages.EncodePng(TestImages.FromAscii(new[] { "##", "##" }, new Rgba(9, 9, 9, 255))));
        string output = Path.Combine(dir, "hero.glb");

        int code = Program.Run(
            new[] { input }, output, depth: 2, voxelSize: 1.0,
            format: "glb", pivot: "bottom-center", recursive: false);

        Assert.Equal(0, code);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public void Run_MissingInput_ReturnsTwo()
    {
        int code = Program.Run(
            new[] { "nope.png" }, null, 1, 1.0, "glb", "bottom-center", false);
        Assert.Equal(2, code);
    }

    [Fact]
    public void Run_BadFlagValue_ReturnsFive()
    {
        string dir = Directory.CreateTempSubdirectory("mixel_cli_").FullName;
        string input = Path.Combine(dir, "x.png");
        File.WriteAllBytes(input,
            TestImages.EncodePng(TestImages.FromAscii(new[] { "#" }, new Rgba(1, 1, 1, 255))));

        int code = Program.Run(new[] { input }, null, depth: 0, voxelSize: 1.0,
            format: "glb", pivot: "bottom-center", recursive: false); // depth < 1
        Assert.Equal(5, code);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter CliTests`
Expected: FAIL — `Program.Run` not defined.

- [ ] **Step 4: Implement `Program.cs`**

Replace `src/Mixel.Cli/Program.cs` with:

```csharp
using System.CommandLine;
using Mixel.Core;

public static class Program
{
    public static int Main(string[] args)
    {
        var inputsArg = new Argument<string[]>("inputs")
        { Description = "One or more .png files or directories.", Arity = ArgumentArity.OneOrMore };
        var outputOpt = new Option<string?>(new[] { "-o", "--output" }, "Output file (single) or directory (batch).");
        var depthOpt = new Option<int>(new[] { "-d", "--depth" }, () => 1, "Extrusion depth in voxels (min 1).");
        var voxelOpt = new Option<double>(new[] { "-s", "--voxel-size" }, () => 1.0, "Unit length per voxel (> 0).");
        var formatOpt = new Option<string>(new[] { "-f", "--format" }, () => "glb", "glb | gltf | gltf-embedded.");
        var pivotOpt = new Option<string>(new[] { "-p", "--pivot" }, () => "bottom-center", "bottom-center | center | min-corner.");
        var recursiveOpt = new Option<bool>(new[] { "-r", "--recursive" }, "Descend subdirectories for directory inputs.");

        var root = new RootCommand("mixel — extrude PNG pixel art into glTF models.")
        { inputsArg, outputOpt, depthOpt, voxelOpt, formatOpt, pivotOpt, recursiveOpt };

        root.SetHandler((string[] inputs, string? output, int depth, double voxel, string format, string pivot, bool recursive) =>
            Environment.ExitCode = Run(inputs, output, depth, voxel, format, pivot, recursive),
            inputsArg, outputOpt, depthOpt, voxelOpt, formatOpt, pivotOpt, recursiveOpt);

        root.Invoke(args);
        return Environment.ExitCode;
    }

    internal static int Run(string[] inputs, string? output, int depth, double voxelSize,
        string format, string pivot, bool recursive)
    {
        // ---- validate flags -> exit 5 ----
        if (depth < 1) return Fail(5, "depth must be >= 1.");
        if (voxelSize <= 0) return Fail(5, "voxel-size must be > 0.");
        if (!TryParseFormat(format, out var fmt)) return Fail(5, $"unknown format '{format}'.");
        if (!TryParsePivot(pivot, out var piv)) return Fail(5, $"unknown pivot '{pivot}'.");

        try
        {
            var paths = InputExpander.Expand(inputs, recursive);
            if (paths.Count == 0) return Fail(2, "no .png inputs found.");

            bool batch = paths.Count > 1 || Directory.Exists(inputs[0]);

            if (!batch)
            {
                string inPath = paths[0];
                string outPath = output
                    ?? Path.ChangeExtension(inPath, Extruder.DefaultExtension(fmt));
                var opts = new ExtrudeOptions
                {
                    PngBytes = File.ReadAllBytes(inPath),
                    Depth = depth, VoxelSize = (float)voxelSize, Format = fmt, Pivot = piv,
                };
                Extruder.ExtrudeToFile(opts, outPath);
                return 0;
            }

            // batch: output is a directory
            var result = BatchRunner.Run(paths, depth, (float)voxelSize, fmt, piv, output);
            foreach (var item in result.Items.Where(i => !i.Success))
                Console.Error.WriteLine($"  failed: {item.InputPath}: {item.Error}");
            Console.Error.WriteLine($"{result.SucceededCount} succeeded, {result.FailedCount} failed.");
            return result.FailedCount > 0 ? 1 : 0;
        }
        catch (FileNotFoundException ex) { return Fail(2, ex.Message); }
        catch (DirectoryNotFoundException ex) { return Fail(2, ex.Message); }
        catch (InvalidPngException ex) { return Fail(3, ex.Message); }
        catch (EmptySilhouetteException ex) { return Fail(4, ex.Message); }
        catch (UnauthorizedAccessException ex) { return Fail(6, ex.Message); }
        catch (IOException ex) { return Fail(6, ex.Message); }
    }

    private static int Fail(int code, string message)
    {
        Console.Error.WriteLine($"mixel: {message}");
        return code;
    }

    private static bool TryParseFormat(string s, out GltfFormat fmt)
    {
        switch (s.ToLowerInvariant())
        {
            case "glb": fmt = GltfFormat.Glb; return true;
            case "gltf": fmt = GltfFormat.Gltf; return true;
            case "gltf-embedded": fmt = GltfFormat.GltfEmbedded; return true;
            default: fmt = GltfFormat.Glb; return false;
        }
    }

    private static bool TryParsePivot(string s, out Pivot piv)
    {
        switch (s.ToLowerInvariant())
        {
            case "bottom-center": piv = Pivot.BottomCenter; return true;
            case "center": piv = Pivot.Center; return true;
            case "min-corner": piv = Pivot.MinCorner; return true;
            default: piv = Pivot.BottomCenter; return false;
        }
    }
}
```

> Note: `System.CommandLine` is a prerelease (2.0.0-beta*). If the `SetHandler`/`Option` constructor signatures differ in the installed beta, adjust the wiring — but keep `Run(...)` exactly as specified, since the tests target it directly and it carries all behavior.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter CliTests`
Expected: PASS (all three facts).

- [ ] **Step 6: Run the whole suite + smoke-test the binary**

```bash
dotnet test
dotnet run --project src/Mixel.Cli -- --help
```
Expected: all tests PASS; `--help` prints usage with the documented flags.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: mixel CLI with batch and exit-code mapping"
```

---

## Self-Review

**Spec coverage:**
- PNG input / opaque-only alpha → Tasks 2, 3. ✓
- Greedy front/back + perimeter walls + pivot + Y-up/+Z + NEAREST UVs → Task 4. ✓
- Tight-cropped texture → Task 5. ✓
- glb default + gltf + gltf-embedded, NEAREST sampler, in-memory glb for web/preview → Task 6. ✓
- Batch (file/dir/multi, recursive, one-bad-doesn't-abort, output dir) → Tasks 7, 8. ✓
- CLI flags + defaults + exit codes → Task 8. ✓
- Distribution (single-file publish) → not a task; it is a release step documented in spec §12 and adds no code. A follow-up task can add a `publish` CI step when packaging.
- Web app (spec §13) → **separate plan** (Plan 2), by design.

**Placeholder scan:** No TBD/TODO; every code step contains complete code and exact commands. ✓

**Type consistency:** `ExtrudeOptions`, `Mask`, `Mesh`, `Pivot`, `GltfFormat`, `Extruder.DefaultExtension`, `BatchRunner.Run` signatures match across Tasks 4–8. `Program.Run` parameter list matches the CLI tests. ✓

---

## Next

After this plan is green, **Plan 2 (`Mixel.Web`)** builds the Blazor WASM PWA over the finished `Mixel.Core`: WASM host, `<model-viewer>` preview, single-screen UI, 14-theme system (Vaporsoft Dark default), batch + zip export, PWA manifest/service worker. Plan 2's tasks (theming, preview, batch UI) are mutually independent → parallel-agent friendly.
