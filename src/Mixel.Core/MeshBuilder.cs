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
