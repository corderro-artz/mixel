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

        // full: 1 greedy rect → 2 face quads (4 tri) + 12 perimeter wall quads (24 tri) = 28
        Assert.Equal(28, full.TriangleCount);
        // ring: 4 greedy rects → 8 face quads (16 tri) + 16 wall quads incl. interior hole sides (32 tri) = 48
        Assert.Equal(48, ring.TriangleCount);
    }
}
