namespace Mixel.Web.Services;

public enum PainterTool { Paint, Erase, Fill }

public sealed class DepthPainterState
{
    public PainterTool Tool        { get; set; } = PainterTool.Paint;
    public byte        ActiveLevel { get; set; } = 1;

    /// <summary>Apply the active tool (Paint or Erase) at (x,y). Returns true if levels was mutated.</summary>
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
            default:
                return false;
        }
    }

    /// <summary>
    /// Cycle the depth level at (x,y): 0/1/2/.../maxDepth wraps back to 1.
    /// Air pixels (solidMask[idx]==false) are no-ops.
    /// Returns true if levels was mutated.
    /// </summary>
    public bool CycleLevel(byte[] levels, bool[] solidMask, int w, int h, int x, int y, int maxDepth)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return false;
        int idx = y * w + x;
        if (!solidMask[idx]) return false;
        byte next = (byte)(levels[idx] % maxDepth + 1);
        if (levels[idx] == next) return false;
        levels[idx] = next;
        return true;
    }

    /// <summary>
    /// Decrease the depth level at (x,y) by 1, minimum 1. Air pixels are no-ops.
    /// Returns true if levels was mutated.
    /// </summary>
    public bool DecreaseLevel(byte[] levels, bool[] solidMask, int w, int h, int x, int y)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return false;
        int idx = y * w + x;
        if (!solidMask[idx] || levels[idx] <= 1) return false;
        levels[idx]--;
        return true;
    }

    /// <summary>
    /// Fill every solid pixel whose RGBA color matches the pixel at (sx,sy) with ActiveLevel.
    /// Returns true if any level was mutated.
    /// </summary>
    public bool ColorFill(byte[] levels, bool[] solidMask, byte[] rgba, int w, int h, int sx, int sy)
    {
        if (sx < 0 || sy < 0 || sx >= w || sy >= h) return false;
        int startIdx = sy * w + sx;
        if (!solidMask[startIdx]) return false;

        int ri0 = startIdx * 4;
        byte tr = rgba[ri0], tg = rgba[ri0 + 1], tb = rgba[ri0 + 2], ta = rgba[ri0 + 3];

        bool changed = false;
        for (int i = 0; i < solidMask.Length; i++)
        {
            if (!solidMask[i]) continue;
            int ri = i * 4;
            if (rgba[ri] == tr && rgba[ri + 1] == tg && rgba[ri + 2] == tb && rgba[ri + 3] == ta
                && levels[i] != ActiveLevel)
            {
                levels[i] = ActiveLevel;
                changed = true;
            }
        }
        return changed;
    }
}
