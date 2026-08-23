using UpBrowser.Rendering;

namespace UpBrowser;

/// <summary>
/// Off-screen rendering command line used by the visual-regression harness.
///
///   UpBrowser --snapshot &lt;input.html&gt; &lt;output.png&gt; [width] [height] [dpiScale]
///   UpBrowser --diff &lt;expected.png&gt; &lt;actual.png&gt; [diff.png] [tolerance]
///
/// The snapshot mode never opens a window, so it is safe to run repeatedly from a
/// script and compare the output against a reference rendering of the same markup.
/// </summary>
internal static class SnapshotCli
{
    public static int Run(string[] args)
    {
        try
        {
            return args[0] switch
            {
                "--snapshot" => RunSnapshot(args),
                "--diff" => RunDiff(args),
                "--dumplayout" => RunDumpLayout(args),
                _ => Usage(),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[snapshot] {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 2;
        }
    }

    private static int RunSnapshot(string[] args)
    {
        if (args.Length < 3) return Usage();

        string inputPath = args[1];
        string outputPath = args[2];
        int width = args.Length > 3 ? int.Parse(args[3]) : 1024;
        int height = args.Length > 4 ? int.Parse(args[4]) : 768;
        float dpiScale = args.Length > 5 ? float.Parse(args[5]) : 1f;

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"[snapshot] input not found: {inputPath}");
            return 1;
        }

        var started = DateTime.UtcNow;
        RenderSnapshot.CaptureFileToFile(inputPath, outputPath, width, height, dpiScale);
        var elapsed = (DateTime.UtcNow - started).TotalMilliseconds;

        Console.WriteLine($"[snapshot] {inputPath} -> {outputPath} ({width}x{height} @{dpiScale}x) in {elapsed:F0} ms");
        return 0;
    }

    private static int RunDiff(string[] args)
    {
        if (args.Length < 3) return Usage();

        string expectedPath = args[1];
        string actualPath = args[2];
        string? diffPath = args.Length > 3 ? args[3] : null;
        int tolerance = args.Length > 4 ? int.Parse(args[4]) : 0;

        foreach (var path in new[] { expectedPath, actualPath })
        {
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"[diff] not found: {path}");
                return 1;
            }
        }

        var result = RenderSnapshot.Compare(expectedPath, actualPath, diffPath, tolerance);
        Console.WriteLine($"[diff] {result}");
        if (diffPath != null && !result.SizeMismatch)
            Console.WriteLine($"[diff] visualization -> {diffPath}");

        return result.SizeMismatch || result.DifferingPixels > 0 ? 1 : 0;
    }

    private static int RunDumpLayout(string[] args)
    {
        if (args.Length < 2) return Usage();

        string inputPath = args[1];
        int width = args.Length > 2 ? int.Parse(args[2]) : 1024;
        int height = args.Length > 3 ? int.Parse(args[3]) : 768;

        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"[dumplayout] input not found: {inputPath}");
            return 1;
        }

        var full = Path.GetFullPath(inputPath);
        var html = File.ReadAllText(full);
        var baseUrl = new Uri(full).AbsoluteUri;

        RenderSnapshot.EnsureInitialized();
        var dm = new UpBrowser.Core.Dom.DocumentManager();
        var load = dm.LoadHtmlAsync(html, baseUrl, width, height, 1f).GetAwaiter().GetResult();

        DumpBox(load.Document.DocumentElement, 0);
        return 0;
    }

    private static void DumpBox(UpBrowser.Core.Dom.Element? element, int depth)
    {
        if (element == null) return;
        var box = element.LayoutBox;
        var indent = new string(' ', depth * 2);
        if (box != null)
        {
            var b = box.BorderBox;
            Console.WriteLine($"{indent}<{element.TagName}> fontSize={element.ComputedStyle?.FontSize} border=({b.Left:F1},{b.Top:F1} {b.Width:F1}x{b.Height:F1}) lineH={box.LineHeight:F1} lines={box.Lines?.Count ?? 0} lineRuns={box.LineRuns?.Count ?? 0}");
            if (box.Lines != null)
            {
                foreach (var line in box.Lines)
                {
                    Console.WriteLine($"{indent}  line y={line.Y:F1} baseline={line.Baseline:F1} h={line.Height:F1} runs={line.Runs.Count}");
                    foreach (var run in line.Runs)
                        Console.WriteLine($"{indent}    run '{run.Text}' x={run.X:F1} w={run.Width:F1} fs={run.FontSize}");
                }
            }
        }
        else
        {
            Console.WriteLine($"{indent}<{element.TagName}> fontSize={element.ComputedStyle?.FontSize} (no box)");
        }
        foreach (var child in element.Children)
            if (child is UpBrowser.Core.Dom.Element ce)
                DumpBox(ce, depth + 1);
    }

    private static int Usage()
    {
        Console.Error.WriteLine("usage:");
        Console.Error.WriteLine("  UpBrowser --snapshot <input.html> <output.png> [width] [height] [dpiScale]");
        Console.Error.WriteLine("  UpBrowser --diff <expected.png> <actual.png> [diff.png] [tolerance]");
        Console.Error.WriteLine("  UpBrowser --dumplayout <input.html> [width] [height]");
        return 64;
    }
}
