using System.CommandLine;
using Mixel.Core;

public static class Program
{
    public static int Main(string[] args)
    {
        var inputsArg = new Argument<string[]>("inputs")
        {
            Description = "One or more .png files or directories.",
            Arity = ArgumentArity.OneOrMore
        };
        var outputOpt = new Option<string?>("-o", "--output")
        {
            Description = "Output file (single) or directory (batch)."
        };
        var depthOpt = new Option<int>("-d", "--depth")
        {
            Description = "Extrusion depth in voxels (min 1).",
            DefaultValueFactory = _ => 1
        };
        var voxelOpt = new Option<double>("-s", "--voxel-size")
        {
            Description = "Unit length per voxel (> 0).",
            DefaultValueFactory = _ => 1.0
        };
        var formatOpt = new Option<string>("-f", "--format")
        {
            Description = "glb | gltf | gltf-embedded.",
            DefaultValueFactory = _ => "glb"
        };
        var pivotOpt = new Option<string>("-p", "--pivot")
        {
            Description = "bottom-center | center | min-corner.",
            DefaultValueFactory = _ => "bottom-center"
        };
        var recursiveOpt = new Option<bool>("-r", "--recursive")
        {
            Description = "Descend subdirectories for directory inputs."
        };
        var allowNonStandardOpt = new Option<bool>("--allow-nonstandard")
        {
            Description = "Allow image sizes that are not a power of two from 8 to 1024 per dimension."
        };

        var root = new RootCommand("mixel — extrude PNG pixel art into glTF models.");
        root.Arguments.Add(inputsArg);
        root.Options.Add(outputOpt);
        root.Options.Add(depthOpt);
        root.Options.Add(voxelOpt);
        root.Options.Add(formatOpt);
        root.Options.Add(pivotOpt);
        root.Options.Add(recursiveOpt);
        root.Options.Add(allowNonStandardOpt);

        root.SetAction(parseResult =>
        {
            string[] inputs = parseResult.GetValue(inputsArg)!;
            string? output = parseResult.GetValue(outputOpt);
            int depth = parseResult.GetValue(depthOpt);
            double voxelSize = parseResult.GetValue(voxelOpt);
            string format = parseResult.GetValue(formatOpt)!;
            string pivot = parseResult.GetValue(pivotOpt)!;
            bool recursive = parseResult.GetValue(recursiveOpt);
            bool allowNonStandard = parseResult.GetValue(allowNonStandardOpt);

            return Run(inputs, output, depth, voxelSize, format, pivot, recursive, allowNonStandard);
        });

        return root.Parse(args).Invoke();
    }

    internal static int Run(string[] inputs, string? output, int depth, double voxelSize,
        string format, string pivot, bool recursive, bool allowNonStandard = false)
    {
        // ---- validate flags -> exit 5 ----
        if (depth < 1) return Fail(5, "depth must be >= 1.");
        if (voxelSize <= 0) return Fail(5, "voxel-size must be > 0.");
        if (!TryParseFormat(format, out var fmt)) return Fail(5, $"unknown format '{format}'.");
        if (!TryParsePivot(pivot, out var piv)) return Fail(5, $"unknown pivot '{pivot}'.");

        try
        {
            var paths = InputExpander.Expand(inputs, recursive);
            if (paths.Count == 0) return Fail(2, "no .png inputs found.");

            bool batch = paths.Count > 1 || Directory.Exists(inputs[0]);

            if (!batch)
            {
                string inPath = paths[0];
                string outPath = output
                    ?? Path.ChangeExtension(inPath, Extruder.DefaultExtension(fmt));
                var opts = new ExtrudeOptions
                {
                    PngBytes = File.ReadAllBytes(inPath),
                    Depth = depth,
                    VoxelSize = (float)voxelSize,
                    Format = fmt,
                    Pivot = piv,
                    AllowNonStandardSize = allowNonStandard,
                };
                Extruder.ExtrudeToFile(opts, outPath);
                return 0;
            }

            // batch: output is a directory
            var result = BatchRunner.Run(paths, depth, (float)voxelSize, fmt, piv, output, allowNonStandard);
            foreach (var item in result.Items.Where(i => !i.Success))
                Console.Error.WriteLine($"  failed: {item.InputPath}: {item.Error}");
            Console.Error.WriteLine($"{result.SucceededCount} succeeded, {result.FailedCount} failed.");
            return result.FailedCount > 0 ? 1 : 0;
        }
        catch (FileNotFoundException ex) { return Fail(2, ex.Message); }
        catch (DirectoryNotFoundException ex) { return Fail(2, ex.Message); }
        catch (InvalidPngException ex) { return Fail(3, ex.Message); }
        catch (InvalidImageSizeException ex) { return Fail(7, ex.Message); }
        catch (EmptySilhouetteException ex) { return Fail(4, ex.Message); }
        catch (UnauthorizedAccessException ex) { return Fail(6, ex.Message); }
        catch (IOException ex) { return Fail(6, ex.Message); }
    }

    private static int Fail(int code, string message)
    {
        Console.Error.WriteLine($"mixel: {message}");
        return code;
    }

    private static bool TryParseFormat(string s, out GltfFormat fmt)
    {
        switch (s.ToLowerInvariant())
        {
            case "glb": fmt = GltfFormat.Glb; return true;
            case "gltf": fmt = GltfFormat.Gltf; return true;
            case "gltf-embedded": fmt = GltfFormat.GltfEmbedded; return true;
            default: fmt = GltfFormat.Glb; return false;
        }
    }

    private static bool TryParsePivot(string s, out Pivot piv)
    {
        switch (s.ToLowerInvariant())
        {
            case "bottom-center": piv = Pivot.BottomCenter; return true;
            case "center": piv = Pivot.Center; return true;
            case "min-corner": piv = Pivot.MinCorner; return true;
            default: piv = Pivot.BottomCenter; return false;
        }
    }
}
