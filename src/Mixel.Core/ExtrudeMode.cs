namespace Mixel.Core;

/// <summary>Controls the Z direction and symmetry of extrusion.</summary>
public enum ExtrudeMode
{
    /// <summary>Extrudes forward. Back face at Z=0, front face at Z=depth.</summary>
    Front,

    /// <summary>Image is the centre plane. Extruded equally in +Z and −Z.
    /// Each pixel with depth D spans [−D/2, +D/2] in Z.</summary>
    Symmetric,

    /// <summary>Like Front but with a 1-voxel solid backing slab at Z=[−1, 0].
    /// Useful for sprites that must look solid from behind.</summary>
    Raised,
}
