namespace Mixel.Core;

public static class InputExpander
{
    public static IReadOnlyList<string> Expand(IEnumerable<string> inputs, bool recursive)
    {
        var result = new List<string>();
        foreach (var input in inputs)
        {
            if (Directory.Exists(input))
            {
                var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                // Enumerate all files and filter case-insensitively so that uppercase
                // extensions (e.g. Hero.PNG) are found on Linux as well as Windows.
                result.AddRange(Directory.EnumerateFiles(input, "*", opt)
                    .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)));
            }
            else if (File.Exists(input))
            {
                if (input.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    result.Add(input);
            }
            else
            {
                throw new FileNotFoundException($"Input not found: {input}", input);
            }
        }
        result.Sort(StringComparer.Ordinal);
        return result;
    }
}
