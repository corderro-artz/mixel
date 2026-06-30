using Mixel.Core;

namespace Mixel.Web.Services;

public sealed class SliceRegion
{
    public required string Name { get; set; }
    public required SpriteRect Rect { get; init; }
}
