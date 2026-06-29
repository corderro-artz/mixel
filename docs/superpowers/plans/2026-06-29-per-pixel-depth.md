# Per-Pixel Depth Extrusion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> **CAVEMAN MODE REQUIRED** for all agents — saves context. Every agent prompt must include "caveman mode active".
> **CONTEXT7** — use the `mcp__plugin_context7_context7__resolve-library-id` + `mcp__plugin_context7_context7__query-docs` tools when you need current API docs for bUnit, Blazor JSInterop, or xUnit. Do not guess API shapes from memory.

**Goal:** Add KenneyShape-style per-pixel depth extrusion to Mixel.Core and Mixel.Web, with an in-browser canvas painter, mode toggle (Simple/Per-pixel), and remove the voxel-size UI control.

**Architecture:** New `DepthMap` core type carries `byte[] Levels` (0=air, 1..N=depth). `MeshBuilder` gets a `BuildFromDepthMap` overload that emits per-level front faces and step side walls. Web adds `DepthPainterState` (pure C# tool logic) and `DepthPainter.razor` (canvas via JS interop). `ExtrudeSettings` gains a mode toggle; `Home.razor` routes to the painter or 3D preview based on mode.

**Tech Stack:** .NET 10 Blazor WASM (`Mixel.Web`), .NET 8 class library (`Mixel.Core`), SharpGLTF 1.0.6, xUnit, bUnit 2.7.2, StbImageWriteSharp (test helpers).

## Global Constraints

- `Mixel.Core` targets `net8.0`. Do NOT retarget it.
- `Mixel.Web` targets `net10.0`.
- No CLI changes (`Mixel.Cli` untouched).
- `VoxelSize` stays in `ExtrudeOptions` and `ExtrudeSettings` (CLI parity) but is no longer shown in the web UI.
- All `DepthLevels` arrays use full image dimensions (no bbox crop) with `OffsetX = OffsetY = 0`.
- `EmptySilhouetteException` is thrown by `BuildFromDepthMap` when `dm.MaxLevel == 0`.
- Conventional Commit messages; commit after every task.
- Tests: xUnit for all .NET logic; bUnit for Blazor components; no Playwright.

---

## File Map

| File | Status | Task |
|------|--------|------|
| `Mixel.Core/DepthMap.cs` | NEW | 1 |
| `tests/Mixel.Tests/DepthMapTests.cs` | NEW | 1 |
| `Mixel.Core/MeshBuilder.cs` | MODIFY | 2 |
| `tests/Mixel.Tests/DepthMapMeshTests.cs` | NEW | 2 |
| `Mixel.Core/ExtrudeOptions.cs` | MODIFY | 3 |
| `Mixel.Core/Extruder.cs` | MODIFY | 3 |
| `tests/Mixel.Tests/ExtruderDepthMapTests.cs` | NEW | 3 |
| `Mixel.Web/Services/DepthPainterState.cs` | NEW | 4 |
| `tests/Mixel.Web.Tests/DepthPainterStateTests.cs` | NEW | 4 |
| `Mixel.Web/Services/FileItem.cs` | MODIFY | 5 |
| `Mixel.Web/Services/ExtrudeSettings.cs` | MODIFY | 5 |
| `Mixel.Web/Services/ExtrusionService.cs` | MODIFY | 5 |
| `tests/Mixel.Web.Tests/ExtrudeSettingsTests.cs` | MODIFY | 5 |
| `tests/Mixel.Web.Tests/ExtrusionServiceTests.cs` | MODIFY | 5 |
| `Mixel.Web/wwwroot/js/mixel.js` | MODIFY | 6 |
| `Mixel.Web/Interop/MixelJs.cs` | MODIFY | 7 |
| `Mixel.Web/Components/DepthPainter.razor` | NEW | 7 |
| `Mixel.Web/Components/OptionsPanel.razor` | MODIFY | 8 |
| `tests/Mixel.Web.Tests/OptionsPanelTests.cs` | MODIFY | 8 |
| `Mixel.Web/Pages/Home.razor` | MODIFY | 9 |
| `Mixel.Web/wwwroot/css/app.css` | MODIFY | 9 |

---

### Task 1: `DepthMap` Core Type

**Files:**
- Create: `src/Mixel.Core/DepthMap.cs`
- Create: `tests/Mixel.Tests/DepthMapTests.cs`

**Interfaces:**
- Produces: `DepthMap` class consumed by Tasks 2, 3, 4, 5

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Mixel.Tests/DepthMapTests.cs
using System;
using Mixel.Core;
using Xunit;

public class DepthMapTests
{
    private static DepthMap Make(byte[] levels, int w, int h) => new()
    {
        Width = w, Height = h, Levels = levels, OffsetX = 0, OffsetY = 0
    };

    [Fact]
    public void At_InBounds_ReturnsCorrectLevel()
    {
        var dm = Make(new byte[] { 0, 1, 2, 3 }, w: 2, h: 2);
        Assert.Equal((byte)0, dm.At(0, 0));
        Assert.Equal((byte)1, dm.At(1, 0));
        Assert.Equal((byte)2, dm.At(0, 1));
        Assert.Equal((byte)3, dm.At(1, 1));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(2, 0)]
    [InlineData(0, 2)]
    public void At_OutOfBounds_ReturnsZero(int x, int y)
    {
        var dm = Make(new byte[] { 1, 1, 1, 1 }, w: 2, h: 2);
        Assert.Equal((byte)0, dm.At(x, y));
    }

    [Fact]
    public void MaxLevel_ReturnsMaxInLevels()
    {
        var dm = Make(new byte[] { 0, 3, 1, 2 }, w: 2, h: 2);
        Assert.Equal(3, dm.MaxLevel);
    }

    [Fact]
    public void MaxLevel_EmptyLevels_ReturnsZero()
    {
        var dm = Make(Array.Empty<byte>(), w: 0, h: 0);
        Assert.Equal(0, dm.MaxLevel);
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL** (`DepthMap not found`)

```
dotnet test tests/Mixel.Tests --filter "DepthMapTests" -v minimal
```

- [ ] **Step 3: Create `DepthMap.cs`**

```csharp
// src/Mixel.Core/DepthMap.cs
using System.Linq;

namespace Mixel.Core;

public sealed class DepthMap
{
    public required int Width   { get; init; }
    public required int Height  { get; init; }
    /// <summary>Row-major. 0 = air, 1..N = depth level.</summary>
    public required byte[] Levels { get; init; }
    public required int OffsetX { get; init; }
    public required int OffsetY { get; init; }

    public byte At(int x, int y)
        => x >= 0 && y >= 0 && x < Width && y < Height
            ? Levels[y * Width + x] : (byte)0;

    public int MaxLevel => Levels.Length == 0 ? 0 : Levels.Max();
}
```

- [ ] **Step 4: Run tests — expect PASS**

```
dotnet test tests/Mixel.Tests --filter "DepthMapTests" -v minimal
```

- [ ] **Step 5: Commit**

```
git add src/Mixel.Core/DepthMap.cs tests/Mixel.Tests/DepthMapTests.cs
git commit -m "feat(core): add DepthMap type for per-pixel depth levels"
```

---

### Task 2: `MeshBuilder.BuildFromDepthMap`

**Files:**
- Modify: `src/Mixel.Core/MeshBuilder.cs`
- Create: `tests/Mixel.Tests/DepthMapMeshTests.cs`

**Interfaces:**
- Consumes: `DepthMap` (Task 1), existing private `AddQuadUv`, `AddVertex`, `AddFrontBack`, `ApplyPivot` in `MeshBuilder`
- Produces: `MeshBuilder.BuildFromDepthMap(DepthMap dm, float voxelSize, Pivot pivot)` — consumed by Task 3

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Mixel.Tests/DepthMapMeshTests.cs
using System.Linq;
using Mixel.Core;
using Xunit;

public class DepthMapMeshTests
{
    // Helper: build DepthMap from a 2-D int array (row 0 = top).
    private static DepthMap DM(int[,] grid)
    {
        int h = grid.GetLength(0), w = grid.GetLength(1);
        var levels = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                levels[y * w + x] = (byte)grid[y, x];
        return new DepthMap { Width = w, Height = h, Levels = levels, OffsetX = 0, OffsetY = 0 };
    }

    // Helper: build Mask from bool[,] (matches SilhouetteMask shape, no crop).
    private static Mask Mk(bool[,] grid)
    {
        int h = grid.GetLength(0), w = grid.GetLength(1);
        var solid = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                solid[y * w + x] = grid[y, x];
        return new Mask { Width = w, Height = h, Solid = solid, OffsetX = 0, OffsetY = 0 };
    }

    [Fact]
    public void SinglePixel_Level1_SameTriangleCount_AsMeshBuilderDepth1()
    {
        var dm   = DM(new int[,] { { 1 } });
        var mask = Mk(new bool[,] { { true } });

        var meshDM  = MeshBuilder.BuildFromDepthMap(dm, 1f, Pivot.MinCorner);
        var meshOld = MeshBuilder.Build(mask, depth: 1, voxelSize: 1f, Pivot.MinCorner);

        Assert.Equal(meshOld.TriangleCount, meshDM.TriangleCount);
    }

    [Fact]
    public void StepWall_RightNeighborShallower_EmitsPartialWall()
    {
        // [2][1]: left pixel depth 2, right pixel depth 1.
        // Right side of left pixel needs a wall from z=1 to z=2.
        var dmStep    = DM(new int[,] { { 2, 1 } });
        var dmUniform = DM(new int[,] { { 2, 2 } });

        var meshStep    = MeshBuilder.BuildFromDepthMap(dmStep, 1f, Pivot.MinCorner);
        var meshUniform = MeshBuilder.BuildFromDepthMap(dmUniform, 1f, Pivot.MinCorner);

        // step has one less wall quad than uniform (the boundary wall is half-height instead of full)
        // both have a wall there but step's is a single quad vs two-step walls — net fewer triangles
        // concrete: step emits 1 boundary wall quad; uniform emits 0 (same level, no wall).
        // So step should have MORE triangles than two separate same-level non-adjacent pixels.
        // Just verify it doesn't throw and produces geometry.
        Assert.True(meshStep.TriangleCount > 0);
        // Left pixel at depth 2 must have its right wall capped at z=2, floor at z=1.
        // Right pixel at depth 1 must NOT emit a left wall (D=1 not > ND=2 → skip).
        // Check vertex Z values: max Z == 2, some vertex at z=1 (step wall bottom).
        var zs = Enumerable.Range(0, meshStep.VertexCount)
                           .Select(i => meshStep.Positions[i * 3 + 2]).ToList();
        Assert.Equal(2f, zs.Max(), precision: 4);
        Assert.Contains(zs, z => Math.Abs(z - 1f) < 0.0001f);
    }

    [Fact]
    public void AirNeighbor_FullHeightWall()
    {
        // Single pixel at level 3. All 4 sides are air. Each wall spans z=0 to z=3.
        var dm   = DM(new int[,] { { 3 } });
        var mesh = MeshBuilder.BuildFromDepthMap(dm, 1f, Pivot.MinCorner);

        var zs = Enumerable.Range(0, mesh.VertexCount)
                           .Select(i => mesh.Positions[i * 3 + 2]).ToList();
        Assert.Equal(3f, zs.Max(), precision: 4);
        Assert.Contains(zs, z => Math.Abs(z - 0f) < 0.0001f); // back face at z=0
        Assert.Equal(12, mesh.TriangleCount); // same as Build(depth=3)
    }

    [Fact]
    public void SameLevelAdjacent_NoWallEmittedBetweenPixels()
    {
        // [1][1]: same level, no wall between them; perimeter only.
        var dm   = DM(new int[,] { { 1, 1 } });
        var mask = Mk(new bool[,] { { true, true } });

        var meshDM  = MeshBuilder.BuildFromDepthMap(dm, 1f, Pivot.MinCorner);
        var meshOld = MeshBuilder.Build(mask, depth: 1, voxelSize: 1f, Pivot.MinCorner);

        // Triangle count must match (same geometry, just different code path).
        Assert.Equal(meshOld.TriangleCount, meshDM.TriangleCount);
    }

    [Fact]
    public void BackFace_AlwaysAtZ0()
    {
        // Multiple pixels at different levels. Back face must be at z=0 for all.
        var dm   = DM(new int[,] { { 1, 3, 2 } });
        var mesh = MeshBuilder.BuildFromDepthMap(dm, 1f, Pivot.MinCorner);

        // Find all vertices facing -Z (normal z < 0): those are back-face vertices.
        var backZ = Enumerable.Range(0, mesh.VertexCount)
                              .Where(i => mesh.Normals[i * 3 + 2] < -0.5f)
                              .Select(i => mesh.Positions[i * 3 + 2])
                              .Distinct().ToList();
        Assert.Single(backZ);
        Assert.Equal(0f, backZ[0], precision: 4);
    }

    [Fact]
    public void Pivot_BottomCenter_UsesMaxLevel_ForZOffset()
    {
        // dm with maxLevel=3. BottomCenter → Z centered on depth=3 → offset = -3*s/2 = -1.5.
        var dm   = DM(new int[,] { { 3 } });
        var mesh = MeshBuilder.BuildFromDepthMap(dm, 1f, Pivot.BottomCenter);

        var zs = Enumerable.Range(0, mesh.VertexCount)
                           .Select(i => mesh.Positions[i * 3 + 2]).ToList();
        Assert.Equal(-1.5f, zs.Min(), precision: 4);
        Assert.Equal( 1.5f, zs.Max(), precision: 4);
    }

    [Fact]
    public void EmptyDepthMap_ThrowsEmptySilhouette()
    {
        var dm = DM(new int[,] { { 0, 0 }, { 0, 0 } });
        Assert.Throws<EmptySilhouetteException>(() =>
            MeshBuilder.BuildFromDepthMap(dm, 1f, Pivot.MinCorner));
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL** (`BuildFromDepthMap not found`)

```
dotnet test tests/Mixel.Tests --filter "DepthMapMeshTests" -v minimal
```

- [ ] **Step 3: Add `BuildFromDepthMap` to `MeshBuilder.cs`**

Add the following public static method. All private helpers (`AddQuadUv`, `AddVertex`, `AddQuad`, `ApplyPivot`) already exist in the class — call them directly.

```csharp
// Add inside the MeshBuilder class (src/Mixel.Core/MeshBuilder.cs)

public static Mesh BuildFromDepthMap(DepthMap dm, float voxelSize, Pivot pivot)
{
    if (dm.MaxLevel == 0) throw new EmptySilhouetteException();

    int w = dm.Width, h = dm.Height;
    float s = voxelSize;
    var mesh = new Mesh();

    // --- Back face (z = 0): greedy merge over all solid pixels ---
    {
        var used = new bool[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (dm.At(x, y) == 0 || used[y * w + x]) continue;
            int x1 = x;
            while (x1 + 1 < w && dm.At(x1 + 1, y) > 0 && !used[y * w + x1 + 1]) x1++;
            int y1 = y;
            bool canGrow = true;
            while (canGrow && y1 + 1 < h)
            {
                for (int xx = x; xx <= x1; xx++)
                    if (dm.At(xx, y1 + 1) == 0 || used[(y1 + 1) * w + xx]) { canGrow = false; break; }
                if (canGrow) y1++;
            }
            for (int yy = y; yy <= y1; yy++)
            for (int xx = x; xx <= x1; xx++)
                used[yy * w + xx] = true;

            // Emit back face only (winding matches existing AddFrontBack back logic).
            float X0 = x * s, X1 = (x1 + 1) * s;
            float Ytop = (h - y) * s, Ybot = (h - 1 - y1) * s;
            float u0 = (float)x / w, u1 = (float)(x1 + 1) / w;
            float vtop = (float)y / h, vbot = (float)(y1 + 1) / h;
            AddQuadUv(mesh, (0, 0, -1),
                (X1, Ybot, 0f), (u1, vbot),
                (X0, Ybot, 0f), (u0, vbot),
                (X0, Ytop, 0f), (u0, vtop),
                (X1, Ytop, 0f), (u1, vtop));
        }
    }

    // --- Front faces: group by level, greedy merge within each level ---
    var distinctLevels = dm.Levels.Distinct().Where(l => l > 0).OrderBy(l => l).ToArray();
    foreach (byte level in distinctLevels)
    {
        float zf = level * s;
        var used = new bool[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (dm.At(x, y) != level || used[y * w + x]) continue;
            int x1 = x;
            while (x1 + 1 < w && dm.At(x1 + 1, y) == level && !used[y * w + x1 + 1]) x1++;
            int y1 = y;
            bool canGrow = true;
            while (canGrow && y1 + 1 < h)
            {
                for (int xx = x; xx <= x1; xx++)
                    if (dm.At(xx, y1 + 1) != level || used[(y1 + 1) * w + xx]) { canGrow = false; break; }
                if (canGrow) y1++;
            }
            for (int yy = y; yy <= y1; yy++)
            for (int xx = x; xx <= x1; xx++)
                used[yy * w + xx] = true;

            // Emit front face only (CCW from +Z).
            float X0 = x * s, X1 = (x1 + 1) * s;
            float Ytop = (h - y) * s, Ybot = (h - 1 - y1) * s;
            float u0 = (float)x / w, u1 = (float)(x1 + 1) / w;
            float vtop = (float)y / h, vbot = (float)(y1 + 1) / h;
            AddQuadUv(mesh, (0, 0, 1),
                (X0, Ybot, zf), (u0, vbot),
                (X1, Ybot, zf), (u1, vbot),
                (X1, Ytop, zf), (u1, vtop),
                (X0, Ytop, zf), (u0, vtop));
        }
    }

    // --- Side walls: per solid pixel, emit wall only where D > neighbor ---
    for (int y = 0; y < h; y++)
    for (int x = 0; x < w; x++)
    {
        byte D = dm.At(x, y);
        if (D == 0) continue;
        float zTop = D * s;
        float xl = x * s, xr = (x + 1) * s;
        float yt = (h - y) * s, yb = (h - 1 - y) * s;
        float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
        byte nd;

        nd = dm.At(x - 1, y); // -X
        if (D > nd)
            AddQuad(mesh, (-1, 0, 0), u, v,
                (xl, yb, nd * s), (xl, yb, zTop), (xl, yt, zTop), (xl, yt, nd * s));

        nd = dm.At(x + 1, y); // +X
        if (D > nd)
            AddQuad(mesh, (1, 0, 0), u, v,
                (xr, yb, zTop), (xr, yb, nd * s), (xr, yt, nd * s), (xr, yt, zTop));

        nd = dm.At(x, y - 1); // +Y (image row above)
        if (D > nd)
            AddQuad(mesh, (0, 1, 0), u, v,
                (xl, yt, zTop), (xr, yt, zTop), (xr, yt, nd * s), (xl, yt, nd * s));

        nd = dm.At(x, y + 1); // -Y (image row below)
        if (D > nd)
            AddQuad(mesh, (0, -1, 0), u, v,
                (xl, yb, nd * s), (xr, yb, nd * s), (xr, yb, zTop), (xl, yb, zTop));
    }

    ApplyPivot(mesh, pivot, w, h, dm.MaxLevel, s);
    return mesh;
}
```

- [ ] **Step 4: Run tests — expect PASS**

```
dotnet test tests/Mixel.Tests --filter "DepthMapMeshTests" -v minimal
```

- [ ] **Step 5: Run full test suite to verify no regressions**

```
dotnet test tests/Mixel.Tests -v minimal
```

Expected: all existing tests pass.

- [ ] **Step 6: Commit**

```
git add src/Mixel.Core/MeshBuilder.cs tests/Mixel.Tests/DepthMapMeshTests.cs
git commit -m "feat(core): MeshBuilder.BuildFromDepthMap with per-pixel Z and step walls"
```

---

### Task 3: Wire `DepthMap` Through `Extruder` + `ExtrudeOptions`

**Files:**
- Modify: `src/Mixel.Core/ExtrudeOptions.cs`
- Modify: `src/Mixel.Core/Extruder.cs`
- Create: `tests/Mixel.Tests/ExtruderDepthMapTests.cs`

**Interfaces:**
- Consumes: `DepthMap` (Task 1), `MeshBuilder.BuildFromDepthMap` (Task 2)
- Produces: `ExtrudeOptions.DepthMap` property; routing in `Extruder.Build`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Mixel.Tests/ExtruderDepthMapTests.cs
using System;
using System.Linq;
using Mixel.Core;
using SharpGLTF.Schema2;
using Xunit;

public class ExtruderDepthMapTests
{
    // 2×2 fully opaque PNG — helper from existing TestImages.
    private static byte[] TwoPng() =>
        TestImages.EncodePng(TestImages.FromAscii(new[] { "##", "##" }, new Rgba(200, 100, 50, 255)));

    private static DepthMap FlatDM(int w, int h, byte level) => new()
    {
        Width = w, Height = h, OffsetX = 0, OffsetY = 0,
        Levels = Enumerable.Repeat(level, w * h).Select(x => (byte)x).ToArray()
    };

    [Fact]
    public void ExtrudeGlb_WithDepthMap_ProducesValidGlb()
    {
        var opts = new ExtrudeOptions
        {
            PngBytes = TwoPng(),
            AllowNonStandardSize = true,
            DepthMap = FlatDM(2, 2, 2),
        };
        var glb = Extruder.ExtrudeGlb(opts);
        var model = ModelRoot.ParseGLB(new ArraySegment<byte>(glb));
        Assert.Single(model.LogicalMeshes);
    }

    [Fact]
    public void ExtrudeGlb_WithNullDepthMap_UsesSimplePath()
    {
        // Null DepthMap → existing path, no exception.
        var opts = new ExtrudeOptions
        {
            PngBytes = TwoPng(),
            AllowNonStandardSize = true,
            Depth = 2,
            DepthMap = null,
        };
        var glb = Extruder.ExtrudeGlb(opts);
        Assert.NotEmpty(glb);
    }

    [Fact]
    public void ExtrudeToMemory_WithDepthMap_Glb_ReturnsNamedFile()
    {
        var opts = new ExtrudeOptions
        {
            PngBytes = TwoPng(),
            AllowNonStandardSize = true,
            Format = GltfFormat.Glb,
            DepthMap = FlatDM(2, 2, 1),
        };
        var files = Extruder.ExtrudeToMemory(opts, "hero");
        Assert.Single(files);
        Assert.Equal("hero.glb", files[0].Name);
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL** (`DepthMap property not found on ExtrudeOptions`)

```
dotnet test tests/Mixel.Tests --filter "ExtruderDepthMapTests" -v minimal
```

- [ ] **Step 3: Add `DepthMap?` property to `ExtrudeOptions`**

```csharp
// src/Mixel.Core/ExtrudeOptions.cs — add one property:
/// <summary>When non-null, activates per-pixel depth mode. Overrides <see cref="Depth"/>.</summary>
public DepthMap? DepthMap { get; init; } = null;
```

Full file after change:

```csharp
namespace Mixel.Core;

public sealed record ExtrudeOptions
{
    public required byte[] PngBytes { get; init; }
    public int Depth { get; init; } = 1;
    public float VoxelSize { get; init; } = 1f;
    public GltfFormat Format { get; init; } = GltfFormat.Glb;
    public Pivot Pivot { get; init; } = Pivot.BottomCenter;
    public bool AllowNonStandardSize { get; init; } = false;
    /// <summary>When non-null, activates per-pixel depth mode. Overrides <see cref="Depth"/>.</summary>
    public DepthMap? DepthMap { get; init; } = null;
}
```

- [ ] **Step 4: Update `Extruder.Build` to route per-pixel path**

```csharp
// src/Mixel.Core/Extruder.cs — replace the Build method:
private static (Mesh mesh, byte[] tex) Build(ExtrudeOptions o)
{
    var img  = PngLoader.Load(o.PngBytes);
    ImageSize.Validate(img.Width, img.Height, o.AllowNonStandardSize);
    var mask = SilhouetteMask.Build(img);
    var tex  = TextureBaker.BakePng(img, mask);
    var mesh = o.DepthMap is not null
        ? MeshBuilder.BuildFromDepthMap(o.DepthMap, o.VoxelSize, o.Pivot)
        : MeshBuilder.Build(mask, o.Depth, o.VoxelSize, o.Pivot);
    return (mesh, tex);
}
```

- [ ] **Step 5: Run new tests — expect PASS**

```
dotnet test tests/Mixel.Tests --filter "ExtruderDepthMapTests" -v minimal
```

- [ ] **Step 6: Run full suite to verify no regressions**

```
dotnet test tests/Mixel.Tests -v minimal
```

- [ ] **Step 7: Commit**

```
git add src/Mixel.Core/ExtrudeOptions.cs src/Mixel.Core/Extruder.cs tests/Mixel.Tests/ExtruderDepthMapTests.cs
git commit -m "feat(core): route per-pixel DepthMap through Extruder pipeline"
```

---

### Task 4: `DepthPainterState` (Pure C# Tool Logic)

**Files:**
- Create: `src/Mixel.Web/Services/DepthPainterState.cs`
- Create: `tests/Mixel.Web.Tests/DepthPainterStateTests.cs`

**Interfaces:**
- Produces: `DepthPainterState`, `PainterTool` enum — consumed by Task 7

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Mixel.Web.Tests/DepthPainterStateTests.cs
using Mixel.Web.Services;
using Xunit;

public class DepthPainterStateTests
{
    // 3×3 grid: centre solid, corners solid, all initially at level 1 except (1,1)=2.
    // solidMask: all true.
    private static (byte[] levels, bool[] solid, int w, int h) Grid3x3()
    {
        int w = 3, h = 3;
        var levels = new byte[] { 1,1,1, 1,2,1, 1,1,1 };
        var solid  = new bool[] { true,true,true, true,true,true, true,true,true };
        return (levels, solid, w, h);
    }

    [Fact]
    public void Paint_SetActiveLevel_AtPixel()
    {
        var (levels, solid, w, h) = Grid3x3();
        var state = new DepthPainterState { Tool = PainterTool.Paint, ActiveLevel = 5 };
        bool changed = state.Apply(levels, solid, w, h, 0, 0);
        Assert.True(changed);
        Assert.Equal(5, levels[0]);
    }

    [Fact]
    public void Paint_SameLevel_NoChange()
    {
        var (levels, solid, w, h) = Grid3x3();
        var state = new DepthPainterState { Tool = PainterTool.Paint, ActiveLevel = 1 };
        bool changed = state.Apply(levels, solid, w, h, 0, 0); // already 1
        Assert.False(changed);
    }

    [Fact]
    public void Paint_AirPixel_IsNoop()
    {
        int w = 2, h = 1;
        var levels = new byte[] { 1, 0 };
        var solid  = new bool[] { true, false }; // (1,0) is air
        var state = new DepthPainterState { Tool = PainterTool.Paint, ActiveLevel = 3 };
        bool changed = state.Apply(levels, solid, w, h, 1, 0);
        Assert.False(changed);
        Assert.Equal(0, levels[1]);
    }

    [Fact]
    public void Erase_SetsZero()
    {
        var (levels, solid, w, h) = Grid3x3();
        var state = new DepthPainterState { Tool = PainterTool.Erase };
        bool changed = state.Apply(levels, solid, w, h, 1, 1); // level 2 → 0
        Assert.True(changed);
        Assert.Equal(0, levels[1 * w + 1]);
    }

    [Fact]
    public void Erase_AlreadyZero_NoChange()
    {
        int w = 1, h = 1;
        var levels = new byte[] { 0 };
        var solid  = new bool[] { false };
        var state = new DepthPainterState { Tool = PainterTool.Erase };
        bool changed = state.Apply(levels, solid, w, h, 0, 0);
        Assert.False(changed);
    }

    [Fact]
    public void Eyedrop_SetsActiveLevel_ReturnsFalse()
    {
        var (levels, solid, w, h) = Grid3x3();
        var state = new DepthPainterState { Tool = PainterTool.Eyedrop, ActiveLevel = 1 };
        bool changed = state.Apply(levels, solid, w, h, 1, 1); // level 2 at (1,1)
        Assert.False(changed);
        Assert.Equal(2, state.ActiveLevel);
    }

    [Fact]
    public void Eyedrop_AirPixel_DoesNotChangeActiveLevel()
    {
        int w = 1, h = 1;
        var levels = new byte[] { 0 };
        var solid  = new bool[] { false };
        var state = new DepthPainterState { Tool = PainterTool.Eyedrop, ActiveLevel = 3 };
        state.Apply(levels, solid, w, h, 0, 0);
        Assert.Equal(3, state.ActiveLevel); // unchanged
    }

    [Fact]
    public void FloodFill_FillsConnectedSameLevel()
    {
        // 3×1: [1][1][2]. Fill (0,0) with level 5 → first two become 5.
        int w = 3, h = 1;
        var levels = new byte[] { 1, 1, 2 };
        var solid  = new bool[] { true, true, true };
        var state = new DepthPainterState { Tool = PainterTool.Fill, ActiveLevel = 5 };
        bool changed = state.Apply(levels, solid, w, h, 0, 0);
        Assert.True(changed);
        Assert.Equal(5, levels[0]);
        Assert.Equal(5, levels[1]);
        Assert.Equal(2, levels[2]); // different level — not filled
    }

    [Fact]
    public void FloodFill_StopsAtBoundary()
    {
        // 3×3: uniform level 1. Fill (0,0) → all nine become active level.
        int w = 3, h = 3;
        var levels = new byte[9]; for (int i = 0; i < 9; i++) levels[i] = 1;
        var solid  = new bool[9]; for (int i = 0; i < 9; i++) solid[i]  = true;
        var state = new DepthPainterState { Tool = PainterTool.Fill, ActiveLevel = 4 };
        state.Apply(levels, solid, w, h, 1, 1);
        Assert.All(levels, l => Assert.Equal(4, l));
    }

    [Fact]
    public void FloodFill_NoCrossDiagonal()
    {
        // Checkerboard:
        // [1][2]
        // [2][1]
        // Fill (0,0) level 1 → only (0,0) changes (no 4-connected same-level neighbour).
        int w = 2, h = 2;
        var levels = new byte[] { 1, 2, 2, 1 };
        var solid  = new bool[] { true, true, true, true };
        var state = new DepthPainterState { Tool = PainterTool.Fill, ActiveLevel = 5 };
        state.Apply(levels, solid, w, h, 0, 0);
        Assert.Equal(5, levels[0]);
        Assert.Equal(2, levels[1]); // unchanged
        Assert.Equal(2, levels[2]); // unchanged
        Assert.Equal(1, levels[3]); // unchanged (diagonal — not connected)
    }

    [Fact]
    public void FloodFill_SameLevel_Noop()
    {
        var (levels, solid, w, h) = Grid3x3();
        var state = new DepthPainterState { Tool = PainterTool.Fill, ActiveLevel = 1 };
        bool changed = state.Apply(levels, solid, w, h, 0, 0); // already 1
        Assert.False(changed);
    }

    [Fact]
    public void FloodFill_AirStartPixel_Noop()
    {
        int w = 1, h = 1;
        var levels = new byte[] { 0 };
        var solid  = new bool[] { false };
        var state = new DepthPainterState { Tool = PainterTool.Fill, ActiveLevel = 2 };
        bool changed = state.Apply(levels, solid, w, h, 0, 0);
        Assert.False(changed);
    }

    [Fact]
    public void Apply_OutOfBounds_ReturnsFalse()
    {
        var (levels, solid, w, h) = Grid3x3();
        var state = new DepthPainterState { Tool = PainterTool.Paint, ActiveLevel = 9 };
        Assert.False(state.Apply(levels, solid, w, h, -1, 0));
        Assert.False(state.Apply(levels, solid, w, h, w, 0));
        Assert.False(state.Apply(levels, solid, w, h, 0, -1));
        Assert.False(state.Apply(levels, solid, w, h, 0, h));
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL**

```
dotnet test tests/Mixel.Web.Tests --filter "DepthPainterStateTests" -v minimal
```

- [ ] **Step 3: Create `DepthPainterState.cs`**

```csharp
// src/Mixel.Web/Services/DepthPainterState.cs
using System.Collections.Generic;

namespace Mixel.Web.Services;

public enum PainterTool { Paint, Erase, Eyedrop, Fill }

public sealed class DepthPainterState
{
    public PainterTool Tool        { get; set; } = PainterTool.Paint;
    public byte        ActiveLevel { get; set; } = 1;

    /// <summary>Apply the active tool at (x,y). Returns true if levels was mutated.</summary>
    public bool Apply(byte[] levels, bool[] solidMask, int w, int h, int x, int y)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return false;
        int idx = y * w + x;
        switch (Tool)
        {
            case PainterTool.Paint:
                if (!solidMask[idx] || levels[idx] == ActiveLevel) return false;
                levels[idx] = ActiveLevel;
                return true;
            case PainterTool.Erase:
                if (levels[idx] == 0) return false;
                levels[idx] = 0;
                return true;
            case PainterTool.Eyedrop:
                if (levels[idx] > 0) ActiveLevel = levels[idx];
                return false;
            case PainterTool.Fill:
                return FloodFill(levels, solidMask, w, h, x, y);
            default:
                return false;
        }
    }

    private bool FloodFill(byte[] levels, bool[] solidMask, int w, int h, int sx, int sy)
    {
        int startIdx = sy * w + sx;
        if (!solidMask[startIdx]) return false;
        byte target = levels[startIdx];
        if (target == ActiveLevel) return false;

        var queue   = new Queue<(int x, int y)>();
        var visited = new bool[w * h];
        queue.Enqueue((sx, sy));
        visited[startIdx] = true;

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            levels[y * w + x] = ActiveLevel;
            foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                int nIdx = ny * w + nx;
                if (visited[nIdx] || levels[nIdx] != target || !solidMask[nIdx]) continue;
                visited[nIdx] = true;
                queue.Enqueue((nx, ny));
            }
        }
        return true;
    }
}
```

- [ ] **Step 4: Run tests — expect PASS**

```
dotnet test tests/Mixel.Web.Tests --filter "DepthPainterStateTests" -v minimal
```

- [ ] **Step 5: Commit**

```
git add src/Mixel.Web/Services/DepthPainterState.cs tests/Mixel.Web.Tests/DepthPainterStateTests.cs
git commit -m "feat(web): DepthPainterState with paint/erase/eyedrop/fill tools"
```

---

### Task 5: `FileItem`, `ExtrudeSettings`, `ExtrusionService` — Web Service Layer

**Files:**
- Modify: `src/Mixel.Web/Services/FileItem.cs`
- Modify: `src/Mixel.Web/Services/ExtrudeSettings.cs`
- Modify: `src/Mixel.Web/Services/ExtrusionService.cs`
- Modify: `tests/Mixel.Web.Tests/ExtrudeSettingsTests.cs`
- Modify: `tests/Mixel.Web.Tests/ExtrusionServiceTests.cs`

**Interfaces:**
- Consumes: `DepthMap` (Task 1), `ExtrudeOptions.DepthMap` (Task 3)
- Produces: `FileItem.DepthLevels`, `FileItem.SolidMask`, `ExtrudeSettings.PerPixelMode`, `ExtrudeSettings.ToOptions(byte[], FileItem?)` — consumed by Tasks 7, 9

- [ ] **Step 1: Write failing tests for `ExtrudeSettings`**

Replace `tests/Mixel.Web.Tests/ExtrudeSettingsTests.cs` entirely:

```csharp
using Mixel.Core;
using Mixel.Web.Services;
using Xunit;

public class ExtrudeSettingsTests
{
    [Fact]
    public void ToOptions_SimpleMode_CopiesAllFields_NullDepthMap()
    {
        var s = new ExtrudeSettings
        {
            Depth = 4, VoxelSize = 2.5, Format = GltfFormat.GltfEmbedded,
            Pivot = Pivot.Center, PerPixelMode = false,
        };
        var png = new byte[] { 1, 2, 3 };
        var o = s.ToOptions(png, item: null);

        Assert.Same(png, o.PngBytes);
        Assert.Equal(4, o.Depth);
        Assert.Equal(2.5f, o.VoxelSize);
        Assert.Equal(GltfFormat.GltfEmbedded, o.Format);
        Assert.Equal(Pivot.Center, o.Pivot);
        Assert.Null(o.DepthMap);
    }

    [Fact]
    public void ToOptions_SimpleMode_ExistingCallSignature_StillWorks()
    {
        // Backward compat: ToOptions(byte[]) without FileItem.
        var s = new ExtrudeSettings { Depth = 2 };
        var o = s.ToOptions(new byte[] { 1 });
        Assert.Equal(2, o.Depth);
        Assert.Null(o.DepthMap);
    }

    [Fact]
    public void ToOptions_PerPixelMode_WithLevels_SetsDepthMap()
    {
        var s = new ExtrudeSettings { PerPixelMode = true };
        var item = new FileItem
        {
            Name = "x.png", Bytes = new byte[] { 1 },
            DepthLevels = new byte[] { 0, 1, 1, 0 },
            DepthWidth = 2, DepthHeight = 2,
            SolidMask = new bool[] { false, true, true, false },
        };
        var o = s.ToOptions(new byte[] { 1 }, item);
        Assert.NotNull(o.DepthMap);
        Assert.Equal(2, o.DepthMap!.Width);
        Assert.Equal(2, o.DepthMap.Height);
        Assert.Equal(new byte[] { 0, 1, 1, 0 }, o.DepthMap.Levels);
    }

    [Fact]
    public void ToOptions_PerPixelMode_NullLevels_FallsBackToSimple()
    {
        var s = new ExtrudeSettings { PerPixelMode = true, Depth = 3 };
        var item = new FileItem { Name = "x.png", Bytes = new byte[] { 1 } };
        var o = s.ToOptions(new byte[] { 1 }, item);
        Assert.Null(o.DepthMap);
        Assert.Equal(3, o.Depth);
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL**

```
dotnet test tests/Mixel.Web.Tests --filter "ExtrudeSettingsTests" -v minimal
```

- [ ] **Step 3: Update `FileItem.cs`**

```csharp
// src/Mixel.Web/Services/FileItem.cs
namespace Mixel.Web.Services;

public sealed class FileItem
{
    public required string Name  { get; init; }
    public required byte[] Bytes { get; init; }
    public bool Selected { get; set; } = true;

    // Per-pixel depth state. Null until per-pixel mode is first activated for this file.
    public byte[]? DepthLevels { get; set; }
    public bool[]? SolidMask   { get; set; }
    public int     DepthWidth  { get; set; }
    public int     DepthHeight { get; set; }
}
```

- [ ] **Step 4: Update `ExtrudeSettings.cs`**

```csharp
// src/Mixel.Web/Services/ExtrudeSettings.cs
using Mixel.Core;

namespace Mixel.Web.Services;

public sealed class ExtrudeSettings
{
    public int Depth { get; set; } = 1;
    public double VoxelSize { get; set; } = 1.0;
    public GltfFormat Format { get; set; } = GltfFormat.Glb;
    public Pivot Pivot { get; set; } = Pivot.BottomCenter;
    public bool AllowNonStandardSize { get; set; } = false;
    public bool PerPixelMode   { get; set; } = false;
    public int  MaxDepthLevels { get; set; } = 16; // choices: 8, 16, 24, 32

    // Backward-compat overload (no FileItem — simple mode).
    public ExtrudeOptions ToOptions(byte[] png) => ToOptions(png, null);

    public ExtrudeOptions ToOptions(byte[] png, FileItem? item)
    {
        DepthMap? dm = null;
        if (PerPixelMode && item?.DepthLevels is not null)
        {
            dm = new DepthMap
            {
                Width   = item.DepthWidth,
                Height  = item.DepthHeight,
                Levels  = item.DepthLevels,
                OffsetX = 0,
                OffsetY = 0,
            };
        }
        return new ExtrudeOptions
        {
            PngBytes            = png,
            Depth               = Depth,
            VoxelSize           = (float)VoxelSize,
            Format              = Format,
            Pivot               = Pivot,
            AllowNonStandardSize = AllowNonStandardSize,
            DepthMap            = dm,
        };
    }
}
```

- [ ] **Step 5: Run settings tests — expect PASS**

```
dotnet test tests/Mixel.Web.Tests --filter "ExtrudeSettingsTests" -v minimal
```

- [ ] **Step 6: Add per-pixel overloads to `ExtrusionService.cs`**

Add `FileItem?` optional param to `PreviewGlb` and `Single`; add a `FileItem`-list overload for `BatchZip`:

```csharp
// src/Mixel.Web/Services/ExtrusionService.cs — replace file content:
using System.IO.Compression;
using Mixel.Core;

namespace Mixel.Web.Services;

public sealed record BatchOutcome(string Input, bool Success, string? Error);

public static class ExtrusionService
{
    public static byte[] PreviewGlb(byte[] png, ExtrudeSettings s, FileItem? item = null)
    {
        var opts = s.ToOptions(png, item) with { Format = GltfFormat.Glb };
        return Extruder.ExtrudeGlb(opts);
    }

    public static IReadOnlyList<MixelFile> Single(byte[] png, string baseName, ExtrudeSettings s, FileItem? item = null)
        => Extruder.ExtrudeToMemory(s.ToOptions(png, item), baseName);

    public static byte[] Zip(IReadOnlyList<Mixel.Core.MixelFile> files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var f in files)
            {
                var e = zip.CreateEntry(f.Name, CompressionLevel.Optimal);
                using var es = e.Open();
                es.Write(f.Bytes, 0, f.Bytes.Length);
            }
        return ms.ToArray();
    }

    public static byte[] BatchZip(
        IReadOnlyList<(string name, byte[] png)> inputs, ExtrudeSettings s,
        out IReadOnlyList<BatchOutcome> outcomes)
        => BatchZip(inputs.Select(i => new FileItem { Name = i.name, Bytes = i.png }).ToList(),
                    s, out outcomes);

    public static byte[] BatchZip(
        IReadOnlyList<FileItem> items, ExtrudeSettings s,
        out IReadOnlyList<BatchOutcome> outcomes)
    {
        var results = new List<BatchOutcome>(items.Count);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in items)
            {
                var baseName = Path.GetFileNameWithoutExtension(item.Name);
                try
                {
                    foreach (var file in Extruder.ExtrudeToMemory(s.ToOptions(item.Bytes, item), baseName))
                    {
                        var entry = zip.CreateEntry(file.Name, CompressionLevel.Optimal);
                        using var es = entry.Open();
                        es.Write(file.Bytes, 0, file.Bytes.Length);
                    }
                    results.Add(new BatchOutcome(item.Name, true, null));
                }
                catch (Exception ex)
                {
                    results.Add(new BatchOutcome(item.Name, false, ex.Message));
                }
            }
        }
        outcomes = results;
        return ms.ToArray();
    }
}
```

- [ ] **Step 7: Add `ExtrusionService` per-pixel test to `ExtrusionServiceTests.cs`**

Add one fact at the end of the existing `ExtrusionServiceTests` class:

```csharp
[Fact]
public void PreviewGlb_WithDepthMap_ProducesValidGlb()
{
    var png = PngFixture.Solid(2, 2, 100, 150, 200);
    var item = new FileItem
    {
        Name = "t.png", Bytes = png,
        DepthLevels = new byte[] { 1, 2, 2, 1 },
        SolidMask   = new bool[] { true, true, true, true },
        DepthWidth = 2, DepthHeight = 2,
    };
    var s = new ExtrudeSettings { PerPixelMode = true, AllowNonStandardSize = true };
    var glb = ExtrusionService.PreviewGlb(png, s, item);
    var model = ModelRoot.ParseGLB(new ArraySegment<byte>(glb));
    Assert.Single(model.LogicalMeshes);
}
```

- [ ] **Step 8: Run web tests — expect PASS**

```
dotnet test tests/Mixel.Web.Tests -v minimal
```

- [ ] **Step 9: Commit**

```
git add src/Mixel.Web/Services/FileItem.cs src/Mixel.Web/Services/ExtrudeSettings.cs src/Mixel.Web/Services/ExtrusionService.cs tests/Mixel.Web.Tests/ExtrudeSettingsTests.cs tests/Mixel.Web.Tests/ExtrusionServiceTests.cs
git commit -m "feat(web): FileItem depth state, ExtrudeSettings mode toggle, ExtrusionService per-pixel overloads"
```

---

### Task 6: Canvas JS Interop (`mixel.js`)

**Files:**
- Modify: `src/Mixel.Web/wwwroot/js/mixel.js`

**Interfaces:**
- Produces: `mixel.initDepthCanvas`, `mixel.renderDepthOverlay`, `mixel.listenCanvasInput` — consumed by Task 7

No automated tests (JS). Verify manually after Task 7 is wired up.

- [ ] **Step 1: Add three functions to `mixel.js`**

Append to the `window.mixel` object (before the closing `}`):

```js
// src/Mixel.Web/wwwroot/js/mixel.js — full file:
window.mixel = {
  setModelSrc: function (el, bytes) {
    if (!el) return;
    if (el.__mixelUrl) URL.revokeObjectURL(el.__mixelUrl);
    const blob = new Blob([bytes], { type: "model/gltf-binary" });
    const url = URL.createObjectURL(blob);
    el.__mixelUrl = url;
    el.setAttribute("src", url);
  },
  downloadFile: function (name, bytes) {
    const blob = new Blob([bytes], { type: "application/octet-stream" });
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url; a.download = name;
    document.body.appendChild(a); a.click(); a.remove();
    URL.revokeObjectURL(url);
  },
  saveTheme: function (id) { try { localStorage.setItem("mixel-theme", id); } catch (e) {} },
  loadTheme: function () { try { return localStorage.getItem("mixel-theme"); } catch (e) { return null; } },
  prefersDark: function () {
    return window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches;
  },
  setThemeAttribute: function (id) { document.documentElement.setAttribute("data-theme", id); },

  // --- depth painter ---
  initDepthCanvas: function (id, pngBytes) {
    const canvas = document.getElementById(id);
    if (!canvas) return;
    const blob = new Blob([new Uint8Array(pngBytes)], { type: "image/png" });
    const url = URL.createObjectURL(blob);
    const img = new Image();
    img.onload = function () {
      canvas.width  = img.naturalWidth;
      canvas.height = img.naturalHeight;
      canvas.getContext("2d").drawImage(img, 0, 0);
      canvas._mixelImg = img;
      URL.revokeObjectURL(url);
    };
    img.src = url;
  },
  renderDepthOverlay: function (id, levels, w, h, maxDepth) {
    const canvas = document.getElementById(id);
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (canvas._mixelImg) ctx.drawImage(canvas._mixelImg, 0, 0);
    const safeMax = Math.max(maxDepth - 1, 1);
    for (let y = 0; y < h; y++) {
      for (let x = 0; x < w; x++) {
        const lv = levels[y * w + x];
        if (!lv) continue;
        const hue = Math.round((lv - 1) / safeMax * 270);
        ctx.fillStyle = "hsla(" + hue + ",80%,60%,0.65)";
        ctx.fillRect(x, y, 1, 1);
      }
    }
  },
  listenCanvasInput: function (id, dotNetRef) {
    const canvas = document.getElementById(id);
    if (!canvas) return;
    const coords = function (e) {
      const r = canvas.getBoundingClientRect();
      return {
        x: Math.floor((e.clientX - r.left) * canvas.width  / r.width),
        y: Math.floor((e.clientY - r.top)  * canvas.height / r.height)
      };
    };
    let down = false;
    canvas.addEventListener("pointerdown", function (e) {
      down = true; canvas.setPointerCapture(e.pointerId);
      const c = coords(e);
      dotNetRef.invokeMethodAsync("OnCanvasInput", c.x, c.y, e.buttons);
      e.preventDefault();
    });
    canvas.addEventListener("pointermove", function (e) {
      if (!down) return;
      const c = coords(e);
      dotNetRef.invokeMethodAsync("OnCanvasInput", c.x, c.y, e.buttons);
      e.preventDefault();
    });
    canvas.addEventListener("pointerup",    function () { down = false; });
    canvas.addEventListener("pointercancel",function () { down = false; });
  }
};
```

- [ ] **Step 2: Build to verify no syntax errors**

```
dotnet build src/Mixel.Web -v minimal
```

Expected: build succeeds (JS is not compiled but any bundling step would catch obvious errors).

- [ ] **Step 3: Commit**

```
git add src/Mixel.Web/wwwroot/js/mixel.js
git commit -m "feat(web): canvas JS interop for depth painter (initDepthCanvas, renderDepthOverlay, listenCanvasInput)"
```

---

### Task 7: `DepthPainter.razor` Component + `MixelJs` Interop Methods

**Files:**
- Modify: `src/Mixel.Web/Interop/MixelJs.cs`
- Create: `src/Mixel.Web/Components/DepthPainter.razor`

**Interfaces:**
- Consumes: `DepthPainterState` (Task 4), `FileItem` (Task 5), `MixelJs` new methods, JS functions (Task 6)
- Produces: `DepthPainter` component — consumed by Task 9

- [ ] **Step 1: Add interop methods to `MixelJs.cs`**

```csharp
// src/Mixel.Web/Interop/MixelJs.cs — full file:
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

    public ValueTask InitDepthCanvasAsync(string id, byte[] pngBytes)
        => _js.InvokeVoidAsync("mixel.initDepthCanvas", id, pngBytes);

    public ValueTask RenderDepthOverlayAsync(string id, byte[] levels, int w, int h, int maxDepth)
        => _js.InvokeVoidAsync("mixel.renderDepthOverlay", id, levels, w, h, maxDepth);
}
```

> Note: `listenCanvasInput` is called directly with `IJSRuntime` from `DepthPainter.razor` to avoid a circular dependency between `Interop` and `Components` namespaces.

- [ ] **Step 2: Create `DepthPainter.razor`**

```razor
@* src/Mixel.Web/Components/DepthPainter.razor *@
@using Microsoft.JSInterop
@using Mixel.Web.Interop
@using Mixel.Web.Services
@inject MixelJs Js
@inject IJSRuntime JS
@implements IAsyncDisposable

<div class="depth-painter">
    <div class="painter-tools">
        @foreach (var (tool, icon, label) in _tools)
        {
            var t = tool;
            <button type="button" class="tool-btn @(State.Tool == t ? "active" : "")"
                    title="@label" @onclick="() => State.Tool = t">@icon</button>
        }
    </div>

    <div class="painter-canvas-wrap">
        <canvas id="@_canvasId"
                style="image-rendering: pixelated; width: @(_w * Scale)px; height: @(_h * Scale)px;" />
    </div>

    <div class="painter-palette">
        @for (byte lv = 1; lv <= MaxDepthLevels; lv++)
        {
            var level = lv;
            <button type="button"
                    class="swatch @(State.ActiveLevel == level ? "active" : "")"
                    style="background: hsl(@Hue(level, MaxDepthLevels),80%,60%)"
                    title="Depth @level"
                    @onclick="() => State.ActiveLevel = level">@level</button>
        }
    </div>
</div>

@code {
    [Parameter] public FileItem?    Item           { get; set; }
    [Parameter] public int          MaxDepthLevels { get; set; } = 16;
    [Parameter] public EventCallback OnChanged     { get; set; }

    public  DepthPainterState State { get; } = new();
    private const int Scale = 8;
    private string _canvasId = "depth-canvas";
    private int _w, _h;
    private DotNetObjectReference<DepthPainter>? _self;

    private static readonly (PainterTool tool, string icon, string label)[] _tools =
    {
        (PainterTool.Paint,    "✏",  "Paint"),
        (PainterTool.Fill,     "⬛", "Flood fill"),
        (PainterTool.Eyedrop,  "🔍", "Eyedropper"),
        (PainterTool.Erase,    "✕",  "Erase"),
    };

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || Item is null) return;
        _w = Item.DepthWidth;
        _h = Item.DepthHeight;
        _self = DotNetObjectReference.Create(this);
        await Js.InitDepthCanvasAsync(_canvasId, Item.Bytes);
        await JS.InvokeVoidAsync("mixel.listenCanvasInput", _canvasId, _self);
        await RefreshOverlayAsync();
    }

    [JSInvokable]
    public async Task OnCanvasInput(int x, int y, int buttons)
    {
        if (Item?.DepthLevels is null || Item.SolidMask is null) return;
        bool changed = State.Apply(Item.DepthLevels, Item.SolidMask, _w, _h, x, y);
        if (changed)
        {
            await RefreshOverlayAsync();
            await OnChanged.InvokeAsync();
        }
    }

    private async Task RefreshOverlayAsync()
    {
        if (Item?.DepthLevels is null) return;
        await Js.RenderDepthOverlayAsync(_canvasId, Item.DepthLevels, _w, _h, MaxDepthLevels);
    }

    private static int Hue(int level, int max) =>
        (int)((level - 1.0) / Math.Max(max - 1, 1) * 270);

    public async ValueTask DisposeAsync() { _self?.Dispose(); await ValueTask.CompletedTask; }
}
```

- [ ] **Step 3: Build to verify**

```
dotnet build src/Mixel.Web -v minimal
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add src/Mixel.Web/Interop/MixelJs.cs src/Mixel.Web/Components/DepthPainter.razor
git commit -m "feat(web): DepthPainter canvas component with tool palette and JS interop"
```

---

### Task 8: `OptionsPanel` — Remove VoxelSize, Add Mode Toggle + MaxDepthLevels

**Files:**
- Modify: `src/Mixel.Web/Components/OptionsPanel.razor`
- Modify: `tests/Mixel.Web.Tests/OptionsPanelTests.cs`

**Interfaces:**
- Consumes: `ExtrudeSettings.PerPixelMode`, `ExtrudeSettings.MaxDepthLevels` (Task 5)
- Produces: updated `OptionsPanel` — consumed by Task 9 via `OptionsWidget`

- [ ] **Step 1: Write updated tests**

Replace `tests/Mixel.Web.Tests/OptionsPanelTests.cs` entirely:

```csharp
using Bunit;
using Mixel.Core;
using Mixel.Web.Components;
using Mixel.Web.Services;
using Xunit;

public class OptionsPanelTests : BunitContext
{
    [Fact]
    public void SimpleMode_IncreasingDepth_UpdatesSettings_AndRaisesOnChanged()
    {
        var settings = new ExtrudeSettings { PerPixelMode = false };
        var raised = false;
        var cut = Render<OptionsPanel>(p => p
            .Add(c => c.Settings, settings)
            .Add(c => c.OnChanged, () => raised = true));

        cut.Find("[data-test='depth'] button[aria-label='Increase depth']").Click();

        Assert.Equal(2, settings.Depth);
        Assert.True(raised);
    }

    [Fact]
    public void SimpleMode_Depth_CannotExceedMax()
    {
        var settings = new ExtrudeSettings { PerPixelMode = false, Depth = 3 };
        var cut = Render<OptionsPanel>(p => p.Add(c => c.Settings, settings));
        var inc = cut.Find("[data-test='depth'] button[aria-label='Increase depth']");
        Assert.True(inc.HasAttribute("disabled"));
    }

    [Fact]
    public void VoxelSize_NotRendered()
    {
        var settings = new ExtrudeSettings();
        var cut = Render<OptionsPanel>(p => p.Add(c => c.Settings, settings));
        Assert.Empty(cut.FindAll("[data-test='voxel']"));
    }

    [Fact]
    public void SelectingFormat_UpdatesSettings()
    {
        var settings = new ExtrudeSettings();
        var cut = Render<OptionsPanel>(p => p.Add(c => c.Settings, settings));
        cut.Find("[data-test='format'] input[value='Gltf']").Change("Gltf");
        Assert.Equal(GltfFormat.Gltf, settings.Format);
    }

    [Fact]
    public void SelectingPivot_UpdatesSettings()
    {
        var settings = new ExtrudeSettings();
        var cut = Render<OptionsPanel>(p => p.Add(c => c.Settings, settings));
        cut.Find("[data-test='pivot'] input[value='Center']").Change("Center");
        Assert.Equal(Pivot.Center, settings.Pivot);
    }

    [Fact]
    public void ModeToggle_ToPerPixel_UpdatesSettings_AndRaisesOnChanged()
    {
        var settings = new ExtrudeSettings { PerPixelMode = false };
        var raised = false;
        var cut = Render<OptionsPanel>(p => p
            .Add(c => c.Settings, settings)
            .Add(c => c.OnChanged, () => raised = true));

        cut.Find("[data-test='mode'] button[data-value='perpixel']").Click();

        Assert.True(settings.PerPixelMode);
        Assert.True(raised);
    }

    [Fact]
    public void SimpleMode_ShowsDepthStepper_HidesMaxDepth()
    {
        var settings = new ExtrudeSettings { PerPixelMode = false };
        var cut = Render<OptionsPanel>(p => p.Add(c => c.Settings, settings));
        Assert.NotEmpty(cut.FindAll("[data-test='depth']"));
        Assert.Empty(cut.FindAll("[data-test='max-depth']"));
    }

    [Fact]
    public void PerPixelMode_ShowsMaxDepthSelect_HidesDepthStepper()
    {
        var settings = new ExtrudeSettings { PerPixelMode = true };
        var cut = Render<OptionsPanel>(p => p.Add(c => c.Settings, settings));
        Assert.Empty(cut.FindAll("[data-test='depth']"));
        Assert.NotEmpty(cut.FindAll("[data-test='max-depth']"));
    }

    [Fact]
    public void MaxDepthSelect_UpdatesSettings()
    {
        var settings = new ExtrudeSettings { PerPixelMode = true, MaxDepthLevels = 16 };
        var raised = false;
        var cut = Render<OptionsPanel>(p => p
            .Add(c => c.Settings, settings)
            .Add(c => c.OnChanged, () => raised = true));

        cut.Find("[data-test='max-depth']").Change("24");

        Assert.Equal(24, settings.MaxDepthLevels);
        Assert.True(raised);
    }
}
```

- [ ] **Step 2: Run tests — expect FAIL** (voxel test now passes, mode tests fail)

```
dotnet test tests/Mixel.Web.Tests --filter "OptionsPanelTests" -v minimal
```

- [ ] **Step 3: Rewrite `OptionsPanel.razor`**

```razor
@* src/Mixel.Web/Components/OptionsPanel.razor *@
@using Mixel.Core
@using Mixel.Web.Services

<div class="options">

    {{!-- Mode toggle --}}
    <div class="field">
        <label>Mode</label>
        <div class="mode-toggle" data-test="mode">
            <button type="button" data-value="simple"
                    class="mode-btn @(!Settings.PerPixelMode ? "active" : "")"
                    @onclick="@(() => Step(() => Settings.PerPixelMode = false))">Simple</button>
            <button type="button" data-value="perpixel"
                    class="mode-btn @(Settings.PerPixelMode ? "active" : "")"
                    @onclick="@(() => Step(() => Settings.PerPixelMode = true))">Per-pixel</button>
        </div>
    </div>

    @if (!Settings.PerPixelMode)
    {
        <div class="field">
            <label>Depth</label>
            <div class="stepper" data-test="depth">
                <button type="button" class="step" aria-label="Decrease depth"
                        disabled="@(Settings.Depth <= DepthMin)"
                        @onclick="@(() => Step(() => Settings.Depth = Math.Max(DepthMin, Settings.Depth - 1)))">−</button>
                <span class="step-val" data-test="depth-val">@Settings.Depth</span>
                <button type="button" class="step" aria-label="Increase depth"
                        disabled="@(Settings.Depth >= DepthMax)"
                        @onclick="@(() => Step(() => Settings.Depth = Math.Min(DepthMax, Settings.Depth + 1)))">+</button>
            </div>
        </div>
    }
    else
    {
        <div class="field">
            <label>Max depth levels</label>
            <select data-test="max-depth"
                    value="@Settings.MaxDepthLevels"
                    @onchange="@(e => Step(() => Settings.MaxDepthLevels = int.Parse(e.Value!.ToString()!)))">
                <option value="8">8</option>
                <option value="16">16</option>
                <option value="24">24</option>
                <option value="32">32</option>
            </select>
        </div>
    }

    <div class="field">
        <label>Format</label>
        <div class="radios" data-test="format">
            @foreach (var (val, text) in Formats)
            {
                <label class="radio">
                    <input type="radio" name="fmt" value="@val"
                           checked="@(Settings.Format == ParseFormat(val))"
                           @onchange="@(() => Step(() => Settings.Format = ParseFormat(val)))" />
                    <span>@text</span>
                </label>
            }
        </div>
    </div>

    <div class="field">
        <label>Pivot</label>
        <div class="radios" data-test="pivot">
            @foreach (var (val, text) in Pivots)
            {
                <label class="radio">
                    <input type="radio" name="pivot" value="@val"
                           checked="@(Settings.Pivot == ParsePivot(val))"
                           @onchange="@(() => Step(() => Settings.Pivot = ParsePivot(val)))" />
                    <span>@text</span>
                </label>
            }
        </div>
    </div>

    <div class="field field-row">
        <label for="allow-nonstd">Allow odd sizes</label>
        <button id="allow-nonstd" type="button" data-test="allow-nonstd"
                class="switch @(Settings.AllowNonStandardSize ? "on" : "off")"
                role="switch" aria-checked="@Settings.AllowNonStandardSize"
                aria-label="Allow non-standard image sizes"
                @onclick="@(() => Step(() => Settings.AllowNonStandardSize = !Settings.AllowNonStandardSize))">
            <span class="knob"></span>
        </button>
    </div>

</div>

@code {
    [Parameter] public ExtrudeSettings Settings { get; set; } = new();
    [Parameter] public EventCallback OnChanged { get; set; }

    private const int DepthMin = 1, DepthMax = 3;

    private static readonly (string val, string text)[] Formats =
    {
        ("Glb", "glb"), ("Gltf", "gltf"), ("GltfEmbedded", "gltf-embedded"),
    };

    private static readonly (string val, string text)[] Pivots =
    {
        ("BottomCenter", "bottom-center"), ("Center", "center"), ("MinCorner", "min-corner"),
    };

    private async Task Step(Action mutate) { mutate(); await OnChanged.InvokeAsync(); }

    public static GltfFormat ParseFormat(string? s) => s switch
    {
        "Gltf" => GltfFormat.Gltf,
        "GltfEmbedded" => GltfFormat.GltfEmbedded,
        _ => GltfFormat.Glb,
    };

    public static Pivot ParsePivot(string? s) => s switch
    {
        "Center" => Pivot.Center,
        "MinCorner" => Pivot.MinCorner,
        _ => Pivot.BottomCenter,
    };
}
```

> Note: Razor uses `@*...*@` for comments, not `{{!-- --}}`. Replace the two comment lines with `@* Mode toggle *@` and remove the `{{!-- --}}` syntax shown above.

- [ ] **Step 4: Run tests — expect PASS**

```
dotnet test tests/Mixel.Web.Tests --filter "OptionsPanelTests" -v minimal
```

- [ ] **Step 5: Run full web test suite**

```
dotnet test tests/Mixel.Web.Tests -v minimal
```

- [ ] **Step 6: Commit**

```
git add src/Mixel.Web/Components/OptionsPanel.razor tests/Mixel.Web.Tests/OptionsPanelTests.cs
git commit -m "feat(web): OptionsPanel mode toggle (Simple/Per-pixel), remove VoxelSize, add MaxDepthLevels"
```

---

### Task 9: `Home.razor` + CSS — Mode-Aware Stage and Side-by-Side Layout

**Files:**
- Modify: `src/Mixel.Web/Pages/Home.razor`
- Modify: `src/Mixel.Web/wwwroot/css/app.css`

**Interfaces:**
- Consumes: `DepthPainter` (Task 7), `ExtrudeSettings.PerPixelMode` (Task 5), `ExtrusionService` updated overloads (Task 5)
- Produces: complete per-pixel workflow in the browser

> Use Context7 to look up `IJSRuntime`, `DotNetObjectReference`, and Blazor component lifecycle if needed.

- [ ] **Step 1: Update `Home.razor`**

```razor
@page "/"
@using Mixel.Core
@using Mixel.Web.Components
@using Mixel.Web.Interop
@using Mixel.Web.Services
@using Mixel.Web.Theming
@inject ThemeState Theme
@inject MixelJs Js
@implements IDisposable

<TopBar>
    @if (_settings.PerPixelMode && !_sideBySide)
    {
        <button class="btn" @onclick="TogglePreviewAsync">
            @(_showingPreview ? "◀ Painter" : "▶ Preview 3D")
        </button>
        <button class="btn icon-btn" title="Toggle split view" @onclick="() => { _sideBySide = true; _showingPreview = false; _ = RegenAsync(); }">⬜</button>
    }
    @if (_settings.PerPixelMode && _sideBySide)
    {
        <button class="btn icon-btn" title="Single view" @onclick="() => _sideBySide = false">⬛</button>
    }
    <button class="btn primary" disabled="@(_selectedCount == 0)" @onclick="ExportAsync">
        @(_selectedCount > 1 ? $"Export {_selectedCount} (.zip)" : "Export")
    </button>
</TopBar>

<div class="workspace">
    <FilePanel Items="_files" SelectedPreview="_selected"
               OnUpload="HandleUpload" OnPreview="SelectAsync" OnToggle="ToggleAsync"
               OnSelectAll="SelectAll" OnDeselectAll="DeselectAll" />

    <section class="stage @(_settings.PerPixelMode && _sideBySide ? "stage-split" : "")">

        @if (_settings.PerPixelMode && (!_showingPreview || _sideBySide))
        {
            @if (CurrentItem is not null)
            {
                <DepthPainter @key="_selected"
                              Item="CurrentItem"
                              MaxDepthLevels="_settings.MaxDepthLevels"
                              OnChanged="RegenAsync" />
            }
            else if (_files.Count == 0)
            {
                <div class="hint">Add pixel art to begin</div>
            }
        }

        <div class="@(_settings.PerPixelMode && !_sideBySide && !_showingPreview ? "hidden" : "")">
            <ModelPreview @ref="_preview" />
            @if (!_settings.PerPixelMode && _files.Count == 0)
            {
                <div class="hint">Add pixel art to begin</div>
            }
        </div>

        @if (_notice is not null)
        {
            <div class="notice">@_notice<button @onclick="() => _notice = null" aria-label="Dismiss">✕</button></div>
        }
        <OptionsWidget Settings="_settings" OnChanged="OnOptionsChangedAsync" />
    </section>
</div>

@code {
    private readonly ExtrudeSettings _settings = new();
    private List<FileItem> _files = new();
    private int _selected = -1;
    private ModelPreview? _preview;
    private string? _notice;
    private bool _sideBySide = false;
    private bool _showingPreview = false;

    private int _selectedCount => _files.Count(f => f.Selected);
    private FileItem? CurrentItem => _selected >= 0 && _selected < _files.Count
        ? _files[_selected] : null;

    protected override async Task OnInitializedAsync()
    {
        Theme.Changed += OnThemeChanged;
        await Theme.InitAsync();
    }

    private async void OnThemeChanged()
    {
        try { await Js.SetThemeAttributeAsync(Theme.CurrentId); await InvokeAsync(StateHasChanged); }
        catch (Exception) { }
    }

    private async Task OnOptionsChangedAsync()
    {
        // When mode changes to per-pixel, init depth levels for current file.
        if (_settings.PerPixelMode && CurrentItem is not null)
            EnsureDepthLevels(CurrentItem);
        _showingPreview = false;
        await RegenAsync();
    }

    private async Task HandleUpload(IReadOnlyList<(string name, byte[] bytes)> files)
    {
        bool wasEmpty = _files.Count == 0;
        foreach (var (name, bytes) in files)
            _files.Add(new FileItem { Name = name, Bytes = bytes });
        if (wasEmpty && _files.Count > 0) _selected = 0;
        if (_settings.PerPixelMode && CurrentItem is not null)
            EnsureDepthLevels(CurrentItem);
        await RegenAsync();
    }

    private async Task SelectAsync(int idx)
    {
        _selected = idx;
        if (_settings.PerPixelMode && CurrentItem is not null)
            EnsureDepthLevels(CurrentItem);
        _showingPreview = false;
        await RegenAsync();
    }

    private Task ToggleAsync(int idx)
    {
        _files[idx].Selected = !_files[idx].Selected;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private void SelectAll() { foreach (var f in _files) f.Selected = true; StateHasChanged(); }
    private void DeselectAll() { foreach (var f in _files) f.Selected = false; StateHasChanged(); }

    private async Task TogglePreviewAsync()
    {
        _showingPreview = !_showingPreview;
        if (_showingPreview) await RegenAsync();
        else StateHasChanged();
    }

    // Initialize DepthLevels + SolidMask from PNG alpha channel if not yet done.
    private static void EnsureDepthLevels(FileItem item)
    {
        if (item.DepthLevels is not null) return;
        var img = PngLoader.Load(item.Bytes);
        int len = img.Width * img.Height;
        var levels = new byte[len];
        var solid  = new bool[len];
        for (int i = 0; i < len; i++)
        {
            bool s   = img.Pixels[i].A == 255;
            levels[i] = s ? (byte)1 : (byte)0;
            solid[i]  = s;
        }
        item.DepthLevels = levels;
        item.SolidMask   = solid;
        item.DepthWidth  = img.Width;
        item.DepthHeight = img.Height;
    }

    private async Task RegenAsync()
    {
        if (_selected < 0 || _selected >= _files.Count || _preview is null) return;
        var item = CurrentItem!;
        // Only regenerate 3D preview when visible.
        if (_settings.PerPixelMode && !_showingPreview && !_sideBySide)
        {
            StateHasChanged();
            return;
        }
        try
        {
            var glb = ExtrusionService.PreviewGlb(item.Bytes, _settings, item);
            await _preview.ShowAsync(glb);
            _notice = null;
        }
        catch (InvalidImageSizeException ex) { _notice = ex.Message; }
        catch { /* invalid/empty image */ }
        StateHasChanged();
    }

    private async Task ExportAsync()
    {
        var selected = _files.Where(f => f.Selected).ToList();
        if (selected.Count == 0) return;
        if (selected.Count == 1)
        {
            var item = selected[0];
            var name = Path.GetFileNameWithoutExtension(item.Name);
            try
            {
                var outFiles = ExtrusionService.Single(item.Bytes, name, _settings, item);
                if (outFiles.Count == 1)
                    await Js.DownloadFileAsync(outFiles[0].Name, outFiles[0].Bytes);
                else
                {
                    var zip = ExtrusionService.Zip(outFiles);
                    await Js.DownloadFileAsync(name + ".zip", zip);
                }
                _notice = null;
            }
            catch (InvalidImageSizeException ex) { _notice = ex.Message; }
        }
        else
        {
            var zip = ExtrusionService.BatchZip(selected, _settings, out var outcomes);
            await Js.DownloadFileAsync("mixel-export.zip", zip);
            var failed = outcomes.Count(o => !o.Success);
            _notice = failed > 0 ? $"{failed} of {outcomes.Count} skipped." : null;
        }
    }

    public void Dispose() => Theme.Changed -= OnThemeChanged;
}
```

- [ ] **Step 2: Add CSS for painter layout to `app.css`**

Append to the end of `src/Mixel.Web/wwwroot/css/app.css`:

```css
/* ---- per-pixel depth painter ---- */
.stage-split { display: grid; grid-template-columns: 1fr 1fr; gap: 1px; }
.hidden { display: none !important; }

.depth-painter { display: flex; flex-direction: column; align-items: center;
    gap: 10px; padding: 16px; overflow: auto; }

.painter-tools { display: flex; gap: 6px; }
.tool-btn { width: 36px; height: 36px; border: 1px solid var(--bd); border-radius: 8px;
    background: var(--panel); color: var(--tx); cursor: pointer; font-size: 16px;
    display: inline-flex; align-items: center; justify-content: center; }
.tool-btn.active { border-color: var(--acc); background: var(--acc); color: var(--acc-tx); }
.tool-btn:hover:not(.active) { border-color: var(--acc); }

.painter-canvas-wrap { border: 1px solid var(--bd); border-radius: 6px; overflow: hidden; line-height: 0; }
.painter-canvas-wrap canvas { display: block; }

.painter-palette { display: flex; flex-wrap: wrap; gap: 4px; max-width: 320px; justify-content: center; }
.swatch { width: 28px; height: 28px; border-radius: 6px; border: 2px solid transparent;
    cursor: pointer; font: 700 10px/28px var(--font-head); color: #fff;
    text-shadow: 0 1px 2px rgba(0,0,0,.6); text-align: center; padding: 0; }
.swatch.active { border-color: var(--tx); }
.swatch:hover { opacity: .85; }

.mode-toggle { display: flex; gap: 0; border: 1px solid var(--bd); border-radius: 8px; overflow: hidden; }
.mode-btn { flex: 1; padding: 7px 12px; border: none; background: var(--panel);
    color: var(--tx); cursor: pointer; font: 600 13px/1 var(--font-body); }
.mode-btn.active { background: var(--acc); color: var(--acc-tx); }
.mode-btn:hover:not(.active) { background: var(--bd); }
```

- [ ] **Step 3: Build and verify**

```
dotnet build src/Mixel.Web -v minimal
```

Expected: 0 errors.

- [ ] **Step 4: Run the app and manually verify the golden paths**

```
dotnet run --project src/Mixel.Web
```

Open `http://localhost:5000`. Verify:

1. **Simple mode (default):** Drop a PNG → 3D preview renders → depth stepper works → voxel size stepper is gone.
2. **Switch to Per-pixel mode:** Options panel shows MaxDepthLevels select; stage shows the depth painter canvas.
3. **Painter — Paint:** Select a depth level swatch; click/drag pixels → overlay colors update.
4. **Painter — Fill:** Select Fill tool; click a region → connected same-level pixels fill.
5. **Painter — Eyedrop:** Click a painted pixel → active level updates to that pixel's level.
6. **Painter — Erase:** Drag over pixels → they return to no-overlay (level 0).
7. **Preview 3D button:** Switches stage to 3D view; model shows stepped extrusion.
8. **Split view:** Click ⬜ → painter left, 3D preview right; painting auto-regenerates preview.
9. **Export:** Export a per-pixel file → valid `.glb` opens in Windows 3D Viewer with depth steps visible.
10. **Switch back to Simple mode:** Depth stepper reappears; export works as before.

- [ ] **Step 5: Run full test suite**

```
dotnet test -v minimal
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```
git add src/Mixel.Web/Pages/Home.razor src/Mixel.Web/wwwroot/css/app.css
git commit -m "feat(web): mode-aware stage with depth painter, split-view, per-pixel export"
```

---

## Self-Review

**Spec coverage check:**

| Spec requirement | Task |
|---|---|
| `DepthMap` type (Width, Height, Levels, OffsetX/Y, At, MaxLevel) | 1 |
| `MeshBuilder.BuildFromDepthMap` (back face greedy, per-level front faces, step walls, pivot) | 2 |
| `EmptySilhouetteException` when MaxLevel=0 | 2 |
| `ExtrudeOptions.DepthMap?` property | 3 |
| `Extruder.Build` routes per-pixel | 3 |
| `DepthPainterState` with all 4 tools | 4 |
| Paint guard (solidMask) | 4 |
| FloodFill 4-connected, no diagonal, same-level noop | 4 |
| `FileItem.DepthLevels`, `SolidMask`, `DepthWidth/Height` | 5 |
| `ExtrudeSettings.PerPixelMode`, `MaxDepthLevels` (8/16/24/32, default 16) | 5 |
| `ExtrudeSettings.ToOptions` overload with `FileItem?` | 5 |
| `ExtrusionService` per-pixel overloads | 5 |
| JS canvas interop (initDepthCanvas, renderDepthOverlay, listenCanvasInput) | 6 |
| `DepthPainter.razor` (canvas, tool buttons, palette 1..MaxDepthLevels, hue per level) | 7 |
| Remove VoxelSize from UI | 8 |
| Mode toggle Simple/Per-pixel in OptionsPanel | 8 |
| MaxDepthLevels select (8/16/24/32) | 8 |
| Simple mode shows depth stepper; per-pixel mode hides it | 8 |
| Mode-aware stage (painter / preview / split) | 9 |
| `EnsureDepthLevels` init from PNG alpha | 9 |
| CSS: split layout, painter, palette, mode toggle | 9 |
| Full tests for all .NET logic | 1–5, 8 |
| Fresh agents per task, caveman mode, Context7 | (header) |

No gaps found.

**Placeholder scan:** None present — all steps contain actual code.

**Type consistency:** `DepthMap`, `DepthPainterState`, `PainterTool`, `FileItem.DepthLevels`/`SolidMask`/`DepthWidth`/`DepthHeight`, `ExtrudeSettings.PerPixelMode`/`MaxDepthLevels`, `ExtrudeSettings.ToOptions(byte[], FileItem?)` — all defined in earlier tasks before first use in later tasks.
