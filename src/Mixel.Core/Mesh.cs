namespace Mixel.Core;

public enum Pivot { BottomCenter, Center, MinCorner }

public sealed class Mesh
{
    public List<float> Positions { get; } = new(); // xyz triples
    public List<float> Normals { get; } = new();   // xyz triples
    public List<float> Uvs { get; } = new();        // uv pairs
    public List<int> Indices { get; } = new();

    public int VertexCount => Positions.Count / 3;
    public int TriangleCount => Indices.Count / 3;
}
