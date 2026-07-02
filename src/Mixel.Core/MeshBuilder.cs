using System.Linq;

namespace Mixel.Core;

public static class MeshBuilder
{
    // -------------------------------------------------------------------------
    // Simple (uniform-depth) build
    // -------------------------------------------------------------------------

    public static Mesh Build(Mask mask, int depth, float voxelSize, Pivot pivot,
        ExtrudeMode mode = ExtrudeMode.Front)
    {
        int w = mask.Width, h = mask.Height;
        float s = voxelSize;

        float zf, zb;
        switch (mode)
        {
            case ExtrudeMode.Symmetric:
                zf =  depth * s / 2f;
                zb = -(depth * s / 2f);
                break;
            case ExtrudeMode.Raised:
                zf = depth * s;
                zb = -s;
                break;
            default: // Front
                zf = depth * s;
                zb = 0f;
                break;
        }

        var mesh = new Mesh();

        // Front and back faces via greedy maximal rectangles over the solid mask.
        foreach (var (x, y, x1, y1) in GreedyRects(w, h, (px, py) => mask.At(px, py)))
            AddFrontBack(mesh, x, y, x1, y1, w, h, s, zf, zb);

        // Perimeter walls: solid pixel touching an empty or out-of-bounds neighbour.
        // In simple mode all boundary neighbours are empty (nd=0), so walls always
        // span the full [zb, zf] range regardless of mode.
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (!mask.At(x, y)) continue;

                float xl = x * s, xr = (x + 1) * s;
                float yt = (h - y) * s, yb = (h - 1 - y) * s;
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;

                if (!mask.At(x - 1, y))
                    AddQuad(mesh, (-1, 0, 0), u, v,
                        (xl, yb, zb), (xl, yb, zf), (xl, yt, zf), (xl, yt, zb));
                if (!mask.At(x + 1, y))
                    AddQuad(mesh, (1, 0, 0), u, v,
                        (xr, yb, zf), (xr, yb, zb), (xr, yt, zb), (xr, yt, zf));
                if (!mask.At(x, y - 1))
                    AddQuad(mesh, (0, 1, 0), u, v,
                        (xl, yt, zf), (xr, yt, zf), (xr, yt, zb), (xl, yt, zb));
                if (!mask.At(x, y + 1))
                    AddQuad(mesh, (0, -1, 0), u, v,
                        (xl, yb, zb), (xr, yb, zb), (xr, yb, zf), (xl, yb, zf));
            }
        }

        ApplyPivot(mesh, pivot, w, h, zb, zf, s);
        return mesh;
    }

    // -------------------------------------------------------------------------
    // Per-pixel depth-map build
    // -------------------------------------------------------------------------

    public static Mesh BuildFromDepthMap(DepthMap dm, float voxelSize, Pivot pivot,
        ExtrudeMode mode = ExtrudeMode.Front)
    {
        if (dm.MaxLevel == 0) throw new EmptySilhouetteException();

        int w = dm.Width, h = dm.Height;
        float s = voxelSize;
        var mesh = new Mesh();

        float ZFront(byte level) => mode == ExtrudeMode.Symmetric ? level * s / 2f : level * s;
        float ZBack(byte level)  => mode switch
        {
            ExtrudeMode.Symmetric => -(level * s / 2f),
            ExtrudeMode.Raised    => -s,
            _                      => 0f,
        };

        // ----- Back faces -----
        if (mode == ExtrudeMode.Symmetric)
        {
            // Each depth level sits at its own back-Z, so we greedy-merge per level.
            foreach (byte level in dm.Levels.Distinct().Where(l => l > 0).OrderBy(l => l))
            {
                float zb = ZBack(level);
                foreach (var (x, y, x1, y1) in GreedyRects(w, h, (px, py) => dm.At(px, py) == level))
                    AddBackFaceRect(mesh, x, y, x1, y1, w, h, s, zb);
            }
        }
        else
        {
            // Front and Raised both have a single flat back plane.
            float zb = ZBack(1);
            foreach (var (x, y, x1, y1) in GreedyRects(w, h, (px, py) => dm.At(px, py) > 0))
                AddBackFaceRect(mesh, x, y, x1, y1, w, h, s, zb);
        }

        // ----- Front faces (per depth level) -----
        foreach (byte level in dm.Levels.Distinct().Where(l => l > 0).OrderBy(l => l))
        {
            float zf = ZFront(level);
            foreach (var (x, y, x1, y1) in GreedyRects(w, h, (px, py) => dm.At(px, py) == level))
                AddFrontFaceRect(mesh, x, y, x1, y1, w, h, s, zf);
        }

        // ----- Side walls -----
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            byte D = dm.At(x, y);
            if (D == 0) continue;

            float xl = x * s, xr = (x + 1) * s;
            float yt = (h - y) * s, yb = (h - 1 - y) * s;
            float u = (x + 0.5f) / w, v = (y + 0.5f) / h;

            EmitSideWalls(mesh, mode, D, s, xl, xr, yt, yb, u, v,
                ndL: dm.At(x - 1, y), ndR: dm.At(x + 1, y),
                ndT: dm.At(x, y - 1), ndB: dm.At(x, y + 1));
        }

        float zMin = mode switch
        {
            ExtrudeMode.Symmetric => ZBack((byte)dm.MaxLevel),
            ExtrudeMode.Raised    => -s,
            _                      => 0f,
        };
        ApplyPivot(mesh, pivot, w, h, zMin, ZFront((byte)dm.MaxLevel), s);
        return mesh;
    }

    // -------------------------------------------------------------------------
    // Side-wall helpers
    // -------------------------------------------------------------------------

    private enum WallDir { Left, Right, Top, Bottom }

    /// <summary>Emits wall quads for all four neighbours of a pixel.</summary>
    private static void EmitSideWalls(Mesh mesh, ExtrudeMode mode,
        byte D, float s, float xl, float xr, float yt, float yb, float u, float v,
        byte ndL, byte ndR, byte ndT, byte ndB)
    {
        EmitWall(mesh, mode, D, ndL, s, u, v, xl, xr, yt, yb, WallDir.Left);
        EmitWall(mesh, mode, D, ndR, s, u, v, xl, xr, yt, yb, WallDir.Right);
        EmitWall(mesh, mode, D, ndT, s, u, v, xl, xr, yt, yb, WallDir.Top);
        EmitWall(mesh, mode, D, ndB, s, u, v, xl, xr, yt, yb, WallDir.Bottom);
    }

    /// <summary>
    /// Emits one (or two, for Symmetric) wall quads on the exposed face between
    /// pixel depth D and neighbour depth nd.
    /// </summary>
    private static void EmitWall(Mesh mesh, ExtrudeMode mode,
        byte D, byte nd, float s, float u, float v,
        float xl, float xr, float yt, float yb, WallDir dir)
    {
        if (D <= nd) return;

        switch (mode)
        {
            case ExtrudeMode.Symmetric:
            {
                float dh = D * s / 2f, ndh = nd * s / 2f;
                if (nd == 0)
                {
                    // Entire face exposed: single span [-D/2, +D/2]
                    EmitWallQuad(mesh, -dh, +dh, u, v, xl, xr, yt, yb, dir);
                }
                else
                {
                    // Two overhangs: back [-D/2, -nd/2] and front [+nd/2, +D/2]
                    EmitWallQuad(mesh, -dh, -ndh, u, v, xl, xr, yt, yb, dir);
                    EmitWallQuad(mesh, +ndh, +dh,  u, v, xl, xr, yt, yb, dir);
                }
                break;
            }
            case ExtrudeMode.Raised:
            {
                // Slab back is at -s for all solid pixels, so the slab portion is
                // only exposed where the neighbour is empty.
                float zLow = nd > 0 ? nd * s : -s;
                EmitWallQuad(mesh, zLow, D * s, u, v, xl, xr, yt, yb, dir);
                break;
            }
            default: // Front
                EmitWallQuad(mesh, nd * s, D * s, u, v, xl, xr, yt, yb, dir);
                break;
        }
    }

    /// <summary>Emits a single quad wall spanning [zLow, zHigh] in the given direction.</summary>
    private static void EmitWallQuad(Mesh mesh, float zLow, float zHigh,
        float u, float v, float xl, float xr, float yt, float yb, WallDir dir)
    {
        switch (dir)
        {
            case WallDir.Left:
                AddQuad(mesh, (-1, 0, 0), u, v,
                    (xl, yb, zLow), (xl, yb, zHigh), (xl, yt, zHigh), (xl, yt, zLow));
                break;
            case WallDir.Right:
                AddQuad(mesh, (1, 0, 0), u, v,
                    (xr, yb, zHigh), (xr, yb, zLow), (xr, yt, zLow), (xr, yt, zHigh));
                break;
            case WallDir.Top:
                AddQuad(mesh, (0, 1, 0), u, v,
                    (xl, yt, zHigh), (xr, yt, zHigh), (xr, yt, zLow), (xl, yt, zLow));
                break;
            case WallDir.Bottom:
                AddQuad(mesh, (0, -1, 0), u, v,
                    (xl, yb, zLow), (xr, yb, zLow), (xr, yb, zHigh), (xl, yb, zHigh));
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Face helpers
    // -------------------------------------------------------------------------

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

    private static void AddFrontFaceRect(Mesh mesh, int x, int y, int x1, int y1,
        int w, int h, float s, float zf)
    {
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

    private static void AddBackFaceRect(Mesh mesh, int x, int y, int x1, int y1,
        int w, int h, float s, float zb)
    {
        float X0 = x * s, X1 = (x1 + 1) * s;
        float Ytop = (h - y) * s, Ybot = (h - 1 - y1) * s;
        float u0 = (float)x / w, u1 = (float)(x1 + 1) / w;
        float vtop = (float)y / h, vbot = (float)(y1 + 1) / h;
        AddQuadUv(mesh, (0, 0, -1),
            (X1, Ybot, zb), (u1, vbot),
            (X0, Ybot, zb), (u0, vbot),
            (X0, Ytop, zb), (u0, vtop),
            (X1, Ytop, zb), (u1, vtop));
    }

    // -------------------------------------------------------------------------
    // Vertex / quad primitives
    // -------------------------------------------------------------------------

    private static void AddQuad(
        Mesh mesh, (float x, float y, float z) n, float u, float v,
        (float, float, float) p0, (float, float, float) p1,
        (float, float, float) p2, (float, float, float) p3)
    {
        AddQuadUv(mesh, n, p0, (u, v), p1, (u, v), p2, (u, v), p3, (u, v));
    }

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
        mesh.Indices.Add(b);
        mesh.Indices.Add(b + 1);
        mesh.Indices.Add(b + 2);
        mesh.Indices.Add(b);
        mesh.Indices.Add(b + 2);
        mesh.Indices.Add(b + 3);
    }

    private static void AddVertex(
        Mesh mesh, (float x, float y, float z) p,
        (float x, float y, float z) n, (float u, float v) uv)
    {
        mesh.Positions.Add(p.x); mesh.Positions.Add(p.y); mesh.Positions.Add(p.z);
        mesh.Normals.Add(n.x);   mesh.Normals.Add(n.y);   mesh.Normals.Add(n.z);
        mesh.Uvs.Add(uv.u);      mesh.Uvs.Add(uv.v);
    }

    // -------------------------------------------------------------------------
    // Greedy rect helper
    // -------------------------------------------------------------------------

    private static System.Collections.Generic.IEnumerable<(int x, int y, int x1, int y1)> GreedyRects(
        int w, int h, System.Func<int, int, bool> pred)
    {
        var used = new bool[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            if (!pred(x, y) || used[y * w + x]) continue;
            int x1 = x;
            while (x1 + 1 < w && pred(x1 + 1, y) && !used[y * w + x1 + 1]) x1++;
            int y1 = y;
            bool canGrow = true;
            while (canGrow && y1 + 1 < h)
            {
                for (int xx = x; xx <= x1; xx++)
                    if (!pred(xx, y1 + 1) || used[(y1 + 1) * w + xx]) { canGrow = false; break; }
                if (canGrow) y1++;
            }
            for (int yy = y; yy <= y1; yy++)
            for (int xx = x; xx <= x1; xx++)
                used[yy * w + xx] = true;
            yield return (x, y, x1, y1);
        }
    }

    // -------------------------------------------------------------------------
    // Pivot
    // -------------------------------------------------------------------------

    private static void ApplyPivot(Mesh mesh, Pivot pivot, int w, int h,
        float zMin, float zMax, float s)
    {
        float ox = 0, oy = 0, oz = 0;
        switch (pivot)
        {
            case Pivot.BottomCenter:
                ox = -w * s / 2f;
                oy = 0;
                oz = -(zMin + zMax) / 2f;
                break;
            case Pivot.Center:
                ox = -w * s / 2f;
                oy = -h * s / 2f;
                oz = -(zMin + zMax) / 2f;
                break;
            case Pivot.MinCorner:
                // Shift so minimum-Z vertex lands at world Z=0.
                ox = 0; oy = 0;
                oz = -zMin;
                break;
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
