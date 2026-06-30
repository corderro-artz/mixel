using Mixel.Core;

namespace Mixel.Web.Services;

/// <summary>JS-free logic behind the spritesheet slicer: region list, auto-naming,
/// overlap rules, and crop extraction. Mirrors DepthPainterState so the feature's
/// behaviour is unit-testable without a browser.</summary>
public sealed class SlicerState
{
    private readonly List<SliceRegion> _regions = new();
    public IReadOnlyList<SliceRegion> Regions => _regions;

    public bool TryAdd(SpriteRect rect, out string? error)
    {
        if (rect.IsEmpty)
        {
            error = "Selection is too small.";
            return false;
        }
        foreach (var r in _regions)
            if (r.Rect.Intersects(rect))
            {
                error = "Sprites can't overlap.";
                return false;
            }
        _regions.Add(new SliceRegion { Name = NextName(), Rect = rect });
        error = null;
        return true;
    }

    public void Remove(SliceRegion region) => _regions.Remove(region);

    public void Rename(SliceRegion region, string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        region.Name = trimmed.Length == 0 ? NextName(region) : trimmed;
    }

    /// <summary>Lowest unused "sprite_NN" name, ignoring <paramref name="excluding"/>.</summary>
    public string NextName(SliceRegion? excluding = null)
    {
        for (int n = 1; ; n++)
        {
            var candidate = $"sprite_{n:D2}";
            bool taken = false;
            foreach (var r in _regions)
            {
                if (ReferenceEquals(r, excluding)) continue;
                if (r.Name == candidate) { taken = true; break; }
            }
            if (!taken) return candidate;
        }
    }

    public bool IsStandardSize(SliceRegion region)
        => ImageSize.IsStandard(region.Rect.Width, region.Rect.Height);

    /// <summary>Crops every region from <paramref name="sheet"/>, de-duplicating
    /// output names within the batch ("hero.png", "hero_2.png", …).</summary>
    public List<(string name, byte[] bytes)> BuildExtract(RgbaImage sheet)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<(string, byte[])>(_regions.Count);
        foreach (var region in _regions)
        {
            var name = region.Name;
            int suffix = 2;
            while (!used.Add(name))
                name = $"{region.Name}_{suffix++}";
            var png = SpritesheetSlicer.Crop(sheet, region.Rect);
            result.Add(($"{name}.png", png));
        }
        return result;
    }
}
