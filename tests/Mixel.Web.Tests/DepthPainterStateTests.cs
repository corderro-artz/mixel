using Mixel.Web.Services;
using Xunit;

public class DepthPainterStateTests
{
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
        bool changed = state.Apply(levels, solid, w, h, 0, 0);
        Assert.False(changed);
    }

    [Fact]
    public void Paint_AirPixel_IsNoop()
    {
        int w = 2, h = 1;
        var levels = new byte[] { 1, 0 };
        var solid  = new bool[] { true, false };
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
        bool changed = state.Apply(levels, solid, w, h, 1, 1);
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
        bool changed = state.Apply(levels, solid, w, h, 1, 1);
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
        Assert.Equal(3, state.ActiveLevel);
    }

    [Fact]
    public void FloodFill_FillsConnectedSameLevel()
    {
        int w = 3, h = 1;
        var levels = new byte[] { 1, 1, 2 };
        var solid  = new bool[] { true, true, true };
        var state = new DepthPainterState { Tool = PainterTool.Fill, ActiveLevel = 5 };
        bool changed = state.Apply(levels, solid, w, h, 0, 0);
        Assert.True(changed);
        Assert.Equal(5, levels[0]);
        Assert.Equal(5, levels[1]);
        Assert.Equal(2, levels[2]);
    }

    [Fact]
    public void FloodFill_StopsAtBoundary()
    {
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
        int w = 2, h = 2;
        var levels = new byte[] { 1, 2, 2, 1 };
        var solid  = new bool[] { true, true, true, true };
        var state = new DepthPainterState { Tool = PainterTool.Fill, ActiveLevel = 5 };
        state.Apply(levels, solid, w, h, 0, 0);
        Assert.Equal(5, levels[0]);
        Assert.Equal(2, levels[1]);
        Assert.Equal(2, levels[2]);
        Assert.Equal(1, levels[3]);
    }

    [Fact]
    public void FloodFill_SameLevel_Noop()
    {
        var (levels, solid, w, h) = Grid3x3();
        var state = new DepthPainterState { Tool = PainterTool.Fill, ActiveLevel = 1 };
        bool changed = state.Apply(levels, solid, w, h, 0, 0);
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
