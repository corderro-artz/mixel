using System.Linq;
using Mixel.Core;
using Xunit;

/// <summary>Tests for Front, Symmetric, and Raised extrusion modes.</summary>
public class ExtrudeModeTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Mask OnePixel() => new Mask
    {
        Width = 1, Height = 1,
        Solid = new[] { true },
        OffsetX = 0, OffsetY = 0,
    };

    private static DepthMap DM(int level) => new DepthMap
    {
        Width = 1, Height = 1,
        Levels = new[] { (byte)level },
    };

    private static (float min, float max) ZRange(Mesh mesh)
    {
        var zs = Enumerable.Range(0, mesh.VertexCount)
                           .Select(i => mesh.Positions[i * 3 + 2]);
        return (zs.Min(), zs.Max());
    }

    // -------------------------------------------------------------------------
    // Build (uniform depth) — Front mode
    // -------------------------------------------------------------------------

    [Fact]
    public void Front_Build_ZSpanFrom0ToDepth()
    {
        var mesh = MeshBuilder.Build(OnePixel(), depth: 2, voxelSize: 1f,
            Pivot.MinCorner, ExtrudeMode.Front);
        var (zMin, zMax) = ZRange(mesh);
        Assert.Equal(0f, zMin, precision: 4);
        Assert.Equal(2f, zMax, precision: 4);
    }

    // -------------------------------------------------------------------------
    // Build (uniform depth) — Symmetric mode
    // -------------------------------------------------------------------------

    [Fact]
    public void Symmetric_Build_ZSpanSymmetricAroundZero()
    {
        // BottomCenter + Symmetric: oz = -(−depth/2 + depth/2)/2 = 0. No z-shift.
        var mesh = MeshBuilder.Build(OnePixel(), depth: 4, voxelSize: 1f,
            Pivot.BottomCenter, ExtrudeMode.Symmetric);
        var (zMin, zMax) = ZRange(mesh);
        Assert.Equal(-2f, zMin, precision: 4);
        Assert.Equal(+2f, zMax, precision: 4);
    }

    [Fact]
    public void Symmetric_Build_BottomCenter_ZCenteredAtZero()
    {
        var mesh = MeshBuilder.Build(OnePixel(), depth: 4, voxelSize: 1f,
            Pivot.BottomCenter, ExtrudeMode.Symmetric);
        var (zMin, zMax) = ZRange(mesh);
        Assert.Equal(-2f, zMin, precision: 4);
        Assert.Equal(+2f, zMax, precision: 4);
    }

    [Fact]
    public void Symmetric_Build_SameTriangleCount_AsFront()
    {
        var front = MeshBuilder.Build(OnePixel(), depth: 3, voxelSize: 1f,
            Pivot.MinCorner, ExtrudeMode.Front);
        var sym   = MeshBuilder.Build(OnePixel(), depth: 3, voxelSize: 1f,
            Pivot.MinCorner, ExtrudeMode.Symmetric);
        Assert.Equal(front.TriangleCount, sym.TriangleCount);
    }

    // -------------------------------------------------------------------------
    // Build (uniform depth) — Raised mode
    // -------------------------------------------------------------------------

    [Fact]
    public void Raised_Build_TotalSpanIsDepthPlusOneSlab()
    {
        // Raised: back at -voxelSize, front at depth*voxelSize.
        // MinCorner shifts zMin to 0, so zMax = (depth + 1) * voxelSize.
        var mesh = MeshBuilder.Build(OnePixel(), depth: 2, voxelSize: 1f,
            Pivot.MinCorner, ExtrudeMode.Raised);
        var (zMin, zMax) = ZRange(mesh);
        Assert.Equal(0f, zMin, precision: 4);
        Assert.Equal(3f, zMax, precision: 4); // depth(2) + slab(1) = 3
    }

    [Fact]
    public void Raised_Build_MinCorner_ShiftsZSlabToZero()
    {
        // MinCorner pivot puts the slab back face at world Z=0.
        var mesh = MeshBuilder.Build(OnePixel(), depth: 1, voxelSize: 2f,
            Pivot.MinCorner, ExtrudeMode.Raised);
        var (zMin, _) = ZRange(mesh);
        Assert.Equal(0f, zMin, precision: 4);
    }

    // -------------------------------------------------------------------------
    // BuildFromDepthMap — Symmetric mode
    // -------------------------------------------------------------------------

    [Fact]
    public void Symmetric_DepthMap_Level2_ZSpanMinusOnePlusOne()
    {
        // BottomCenter + Symmetric: oz = 0. Z stays symmetric at [−level/2, +level/2].
        var mesh = MeshBuilder.BuildFromDepthMap(DM(2), 1f, Pivot.BottomCenter, ExtrudeMode.Symmetric);
        var (zMin, zMax) = ZRange(mesh);
        Assert.Equal(-1f, zMin, precision: 4);
        Assert.Equal(+1f, zMax, precision: 4);
    }

    [Fact]
    public void Symmetric_DepthMap_BackFaceNormal_IsNegativeZ()
    {
        // BottomCenter + Symmetric: back face at z = −(level * s / 2) = −1.
        var mesh = MeshBuilder.BuildFromDepthMap(DM(2), 1f, Pivot.BottomCenter, ExtrudeMode.Symmetric);
        var backZ = Enumerable.Range(0, mesh.VertexCount)
                              .Where(i => mesh.Normals[i * 3 + 2] < -0.5f)
                              .Select(i => mesh.Positions[i * 3 + 2])
                              .Distinct().ToList();
        Assert.Single(backZ);
        Assert.Equal(-1f, backZ[0], precision: 4);
    }

    [Fact]
    public void Symmetric_DepthMap_BottomCenter_ZCenteredAtZero()
    {
        // Single pixel at depth=2: symmetric spans [−1, +1]. BottomCenter oz=0.
        var mesh = MeshBuilder.BuildFromDepthMap(DM(2), 1f, Pivot.BottomCenter, ExtrudeMode.Symmetric);
        var (zMin, zMax) = ZRange(mesh);
        Assert.Equal(-1f, zMin, precision: 4);
        Assert.Equal(+1f, zMax, precision: 4);
    }

    // -------------------------------------------------------------------------
    // BuildFromDepthMap — Raised mode
    // -------------------------------------------------------------------------

    [Fact]
    public void Raised_DepthMap_TotalSpanIsMaxLevelPlusOneSlab()
    {
        // Raised: slab back at -voxelSize, front at maxLevel * voxelSize.
        // MinCorner shifts zMin=−1 to 0, so zMax = (3 + 1) = 4.
        var mesh = MeshBuilder.BuildFromDepthMap(DM(3), 1f, Pivot.MinCorner, ExtrudeMode.Raised);
        var (zMin, zMax) = ZRange(mesh);
        Assert.Equal(0f, zMin, precision: 4);
        Assert.Equal(4f, zMax, precision: 4); // maxLevel(3) + slab(1) = 4
    }

    [Fact]
    public void Raised_DepthMap_SpanExceedsFront_ByExactlyOneVoxel()
    {
        // With MinCorner pivot zMin=0 for both modes.
        // Front DM(2): span = 2. Raised DM(2): span = 3 (2 + 1 slab).
        var front  = MeshBuilder.BuildFromDepthMap(DM(2), 1f, Pivot.MinCorner, ExtrudeMode.Front);
        var raised = MeshBuilder.BuildFromDepthMap(DM(2), 1f, Pivot.MinCorner, ExtrudeMode.Raised);
        var (_, frontMax)  = ZRange(front);
        var (_, raisedMax) = ZRange(raised);
        Assert.Equal(frontMax + 1f, raisedMax, precision: 4);
    }

    // -------------------------------------------------------------------------
    // Existing Front DepthMap behaviour unchanged
    // -------------------------------------------------------------------------

    [Fact]
    public void Front_DepthMap_BackFaceAtZero_Unchanged()
    {
        var mesh = MeshBuilder.BuildFromDepthMap(DM(3), 1f, Pivot.MinCorner, ExtrudeMode.Front);
        var backZ = Enumerable.Range(0, mesh.VertexCount)
                              .Where(i => mesh.Normals[i * 3 + 2] < -0.5f)
                              .Select(i => mesh.Positions[i * 3 + 2])
                              .Distinct().ToList();
        Assert.Single(backZ);
        Assert.Equal(0f, backZ[0], precision: 4);
    }

    // -------------------------------------------------------------------------
    // Per-pixel Symmetric step-wall geometry
    // -------------------------------------------------------------------------

    [Fact]
    public void Symmetric_DepthMap_StepWall_TwoSpans_WhenNeighborNonZero()
    {
        // Two adjacent pixels: D=4 (left) and D=2 (right).
        // Symmetric: left spans [−2,+2], right spans [−1,+1].
        // Exposed wall on the left pixel's right face should have TWO quads (two Z overhangs).
        var dm = new DepthMap
        {
            Width = 2, Height = 1,
            Levels = new byte[] { 4, 2 },
        };
        var meshStep = MeshBuilder.BuildFromDepthMap(dm, 1f, Pivot.MinCorner, ExtrudeMode.Symmetric);
        var meshSame = MeshBuilder.BuildFromDepthMap(
            new DepthMap { Width = 2, Height = 1, Levels = new byte[] { 2, 2 } },
            1f, Pivot.MinCorner, ExtrudeMode.Symmetric);

        // Step variant has more triangles (extra overhang quads).
        Assert.True(meshStep.TriangleCount > meshSame.TriangleCount,
            "Step walls in symmetric mode should emit two quads (front + back overhangs).");

        // MinCorner shifts by +2 (zMin=−2 from D=4 symmetric). After: D=4 spans [0, 4].
        var zs = Enumerable.Range(0, meshStep.VertexCount)
                           .Select(i => meshStep.Positions[i * 3 + 2]).ToList();
        Assert.Equal(4f, zs.Max(), precision: 4);
        Assert.Equal(0f, zs.Min(), precision: 4);
    }
}
