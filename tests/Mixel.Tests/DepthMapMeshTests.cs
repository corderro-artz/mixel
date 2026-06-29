using System;
using System.Linq;
using Mixel.Core;
using Xunit;

public class DepthMapMeshTests
{
    private static DepthMap DM(int[,] grid)
    {
        int h = grid.GetLength(0), w = grid.GetLength(1);
        var levels = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                levels[y * w + x] = (byte)grid[y, x];
        return new DepthMap { Width = w, Height = h, Levels = levels, OffsetX = 0, OffsetY = 0 };
    }

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
        var dmStep = DM(new int[,] { { 2, 1 } });
        var mesh = MeshBuilder.BuildFromDepthMap(dmStep, 1f, Pivot.MinCorner);
        Assert.True(mesh.TriangleCount > 0);
        var zs = Enumerable.Range(0, mesh.VertexCount)
                           .Select(i => mesh.Positions[i * 3 + 2]).ToList();
        Assert.Equal(2f, zs.Max(), precision: 4);
        Assert.Contains(zs, z => Math.Abs(z - 1f) < 0.0001f);
    }

    [Fact]
    public void AirNeighbor_FullHeightWall()
    {
        var dm   = DM(new int[,] { { 3 } });
        var mesh = MeshBuilder.BuildFromDepthMap(dm, 1f, Pivot.MinCorner);
        var zs = Enumerable.Range(0, mesh.VertexCount)
                           .Select(i => mesh.Positions[i * 3 + 2]).ToList();
        Assert.Equal(3f, zs.Max(), precision: 4);
        Assert.Contains(zs, z => Math.Abs(z - 0f) < 0.0001f);
        Assert.Equal(12, mesh.TriangleCount);
    }

    [Fact]
    public void SameLevelAdjacent_NoWallEmittedBetweenPixels()
    {
        var dm   = DM(new int[,] { { 1, 1 } });
        var mask = Mk(new bool[,] { { true, true } });
        var meshDM  = MeshBuilder.BuildFromDepthMap(dm, 1f, Pivot.MinCorner);
        var meshOld = MeshBuilder.Build(mask, depth: 1, voxelSize: 1f, Pivot.MinCorner);
        Assert.Equal(meshOld.TriangleCount, meshDM.TriangleCount);
    }

    [Fact]
    public void BackFace_AlwaysAtZ0()
    {
        var dm   = DM(new int[,] { { 1, 3, 2 } });
        var mesh = MeshBuilder.BuildFromDepthMap(dm, 1f, Pivot.MinCorner);
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
