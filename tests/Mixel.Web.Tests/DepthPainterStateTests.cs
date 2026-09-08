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

    // Each pixel is a unique color: R=index, G=0, B=0, A=255
    private static byte[] UniqueColorRgba(int count)
    {
        var rgba = new byte[count * 4];
        for (int i = 0; i < count; i++) { rgba[i * 4] = (byte)i; rgba[i * 4 + 3] = 255; }
        return rgba;
    }

    // All pixels share the same color
    private static byte[] SameColorRgba(int count, byte r = 100, byte g = 50, byte b = 200, byte a = 255)
    {
        var rgba = new byte[count * 4];
        for (int i = 0; i < count; i++) { rgba[i*4]=r; rgba[i*4+1]=g; rgba[i*4+2]=b; rgba[i*4+3]=a; }
        return rgba;
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

    // ----- Erase -----

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

    // ----- CycleLevel -----

    [Fact]
    public void CycleLevel_IncrementsLevel()
    {
        int w = 1, h = 1;
        var levels = new byte[] { 1 };
        var solid  = new bool[] { true };
        var state = new DepthPainterState();
        bool changed = state.CycleLevel(levels, solid, w, h, 0, 0, 16);
        Assert.True(changed);
        Assert.Equal(2, levels[0]);
    }

    [Fact]
    public void CycleLevel_WrapsAtMaxDepth()
    {
        int w = 1, h = 1;
        var levels = new byte[] { 16 };
        var solid  = new bool[] { true };
        var state = new DepthPainterState();
        bool changed = state.CycleLevel(levels, solid, w, h, 0, 0, 16);
        Assert.True(changed);
        Assert.Equal(1, levels[0]);
    }

    [Fact]
    public void CycleLevel_AirPixel_IsNoop()
    {
        int w = 1, h = 1;
        var levels = new byte[] { 0 };
        var solid  = new bool[] { false };
        var state = new DepthPainterState();
        bool changed = state.CycleLevel(levels, solid, w, h, 0, 0, 16);
        Assert.False(changed);
        Assert.Equal(0, levels[0]);
    }

    [Fact]
    public void CycleLevel_OutOfBounds_ReturnsFalse()
    {
        var (levels, solid, w, h) = Grid3x3();
        var state = new DepthPainterState();
        Assert.False(state.CycleLevel(levels, solid, w, h, -1, 0, 16));
        Assert.False(state.CycleLevel(levels, solid, w, h, w, 0, 16));
    }

    // ----- DecreaseLevel -----

    [Fact]
    public void DecreaseLevel_DecrementsByOne()
    {
        int w = 1, h = 1;
        var levels = new byte[] { 3 };
        var solid  = new bool[] { true };
        var state = new DepthPainterState();
        bool changed = state.DecreaseLevel(levels, solid, w, h, 0, 0);
        Assert.True(changed);
        Assert.Equal(2, levels[0]);
    }

    [Fact]
    public void DecreaseLevel_AtOne_IsNoop()
    {
        int w = 1, h = 1;
        var levels = new byte[] { 1 };
        var solid  = new bool[] { true };
        var state = new DepthPainterState();
        bool changed = state.DecreaseLevel(levels, solid, w, h, 0, 0);
        Assert.False(changed);
        Assert.Equal(1, levels[0]);
    }

    [Fact]
    public void DecreaseLevel_AirPixel_IsNoop()
    {
        int w = 1, h = 1;
        var levels = new byte[] { 0 };
        var solid  = new bool[] { false };
        var state = new DepthPainterState();
        bool changed = state.DecreaseLevel(levels, solid, w, h, 0, 0);
        Assert.False(changed);
    }

    [Fact]
    public void DecreaseLevel_OutOfBounds_ReturnsFalse()
    {
        var (levels, solid, w, h) = Grid3x3();
        var state = new DepthPainterState();
        Assert.False(state.DecreaseLevel(levels, solid, w, h, -1, 0));
        Assert.False(state.DecreaseLevel(levels, solid, w, h, w, 0));
    }

    // ----- ColorFill -----

    [Fact]
    public void ColorFill_FillsAllMatchingColorPixels()
    {
        int w = 3, h = 1;
        var levels = new byte[] { 1, 1, 2 };
        var solid  = new bool[] { true, true, true };
        // pixels 0 and 1 share color (R=10), pixel 2 differs (R=20)
        var rgba = new byte[] { 10,0,0,255, 10,0,0,255, 20,0,0,255 };
        var state = new DepthPainterState { Tool = PainterTool.Fill, ActiveLevel = 5 };
        bool changed = state.ColorFill(levels, solid, rgba, w, h, 0, 0);
        Assert.True(changed);
        Assert.Equal(5, levels[0]);
        Assert.Equal(5, levels[1]);
        Assert.Equal(2, levels[2]); // different color — untouched
    }

    [Fact]
    public void ColorFill_SameColorAndLevel_NoChange()
    {
        int w = 2, h = 1;
        var levels = new byte[] { 3, 3 };
        var solid  = new bool[] { true, true };
        var rgba   = SameColorRgba(2);
        var state  = new DepthPainterState { Tool = PainterTool.Fill, ActiveLevel = 3 };
        bool changed = state.ColorFill(levels, solid, rgba, w, h, 0, 0);
        Assert.False(changed);
    }

    [Fact]
    public void ColorFill_AirStartPixel_IsNoop()
    {
        int w = 1, h = 1;
        var levels = new byte[] { 0 };
        var solid  = new bool[] { false };
        var rgba   = SameColorRgba(1);
        var state  = new DepthPainterState { Tool = PainterTool.Fill, ActiveLevel = 2 };
        bool changed = state.ColorFill(levels, solid, rgba, w, h, 0, 0);
        Assert.False(changed);
    }

    [Fact]
    public void ColorFill_SkipsAirPixelsWithMatchingColor()
    {
        int w = 2, h = 1;
        var levels = new byte[] { 1, 1 };
        var solid  = new bool[] { true, false }; // pixel 1 is air despite same color
        var rgba   = SameColorRgba(2);
        var state  = new DepthPainterState { Tool = PainterTool.Fill, ActiveLevel = 4 };
        state.ColorFill(levels, solid, rgba, w, h, 0, 0);
        Assert.Equal(4, levels[0]);
        Assert.Equal(1, levels[1]); // air pixel untouched
    }

    [Fact]
    public void ColorFill_NonAdjacentSameColorPixels_BothFilled()
    {
        int w = 3, h = 1;
        var levels = new byte[] { 1, 1, 1 };
        var solid  = new bool[] { true, true, true };
        // pixel 0 and 2 share a color; pixel 1 differs
        var rgba = new byte[] { 5,0,0,255, 9,0,0,255, 5,0,0,255 };
        var state = new DepthPainterState { Tool = PainterTool.Fill, ActiveLevel = 7 };
        state.ColorFill(levels, solid, rgba, w, h, 0, 0);
        Assert.Equal(7, levels[0]);
        Assert.Equal(1, levels[1]); // different color
        Assert.Equal(7, levels[2]); // non-adjacent but same color — still filled
    }

    // ----- Apply bounds -----

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
