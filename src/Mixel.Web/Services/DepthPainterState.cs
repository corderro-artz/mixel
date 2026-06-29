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
