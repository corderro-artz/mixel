namespace Mixel.Web.Services;

public sealed class FileItem
{
    public required string Name { get; init; }
    public required byte[] Bytes { get; init; }
    public bool Selected { get; set; } = true;
}
