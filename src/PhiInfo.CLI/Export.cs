using System;
using System.CommandLine;
using System.IO;
using PhiInfo.Core;
using PhiInfo.Processing;
using PhiInfo.Processing.Type;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;

namespace PhiInfo.CLI;

internal static class Export
{
    private static readonly Option<string> OutputOption = new("--output")
    {
        Aliases = { "-o" },
        Description = "Output directory",
        DefaultValueFactory = _ => "./output"
    };

    public static readonly Command Command = new("export", "Run export mode commands")
    {
        Options =
        {
            OutputOption
        },
        Action = new CommandLineAction(HandleCommand)
    };

    private static int HandleCommand(ParseResult parseResult)
    {
        var manager = Configuration.Default.ImageFormatsManager;
        var output = parseResult.GetValue(OutputOption)!;
        var format = parseResult.GetValue(Program.ImageFormatOption)!;
        RunExportMode(Program.GetContext(parseResult), output, manager.FindByName(format)!);
        return 0;
    }

    private static void RunExportMode(PhiInfoContext context, string localOutput, IImageFormat format)
    {
        var exporter = new PhiInfoExport(context, "Export", format);
        var writer = new LocalOutputWriter(localOutput);
        exporter.Export(writer);
        context.Dispose();

        Console.WriteLine("OK");
    }

    private sealed class LocalOutputWriter(string rootPath) : IOutputWriter
    {
        public Stream Create(string path, string mime)
        {
            var fullPath = Path.Combine(rootPath, path);
            var parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            return File.Create(fullPath);
        }
    }
}