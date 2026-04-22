using System;
using System.CommandLine;
using System.CommandLine.Completions;
using System.IO;
using System.Linq;
using PhiInfo.Core;
using PhiInfo.Processing.DataProvider;
using Shua.Zip;
using Shua.Zip.ReadAt;
using SixLabors.ImageSharp;

namespace PhiInfo.CLI;

internal class Program
{
    private static readonly Option<ShuaZip[]> PackagesOption = new("--package")
    {
        Aliases = { "-p" },
        Description = "Local paths or HTTP URLs to package files (optional OBBs; order: patch OBB, main OBB, APK)",
        Required = true,
        CustomParser = result =>
        {
            try
            {
                var zips = result.Tokens.Select(token => new ShuaZip(CreateReadAt(token.Value))).ToArray();
                if (zips.Length == 0)
                {
                    result.AddError("Error: No packages provided");
                    return null;
                }

                return zips;
            }
            catch (Exception ex)
            {
                result.AddError($"Error parsing package: {ex.Message}");
                return null;
            }
        }
    };


    private static readonly Option<FileInfo> ClassDataOption = new("--classdata")
    {
        Aliases = { "-cldb" },
        Description = "Path to the class data TPK file",
        DefaultValueFactory = _ => new FileInfo("./classdata.tpk"),
        Validators =
        {
            result =>
            {
                var file = result.GetValueOrDefault<FileInfo>();

                if (!file.Exists) result.AddError($"File not found: {file.FullName}");
            }
        }
    };

    internal static readonly Option<string> ImageFormatOption = new("--image-format")
    {
        Aliases = { "-if" },
        Description = "Image Format",
        DefaultValueFactory = _ => "JPEG",
        CompletionSources =
        {
            _ =>
            {
                return Configuration.Default.ImageFormatsManager.ImageFormats
                    .Select(v => new CompletionItem(v.Name));
            }
        },
        CustomParser = result =>
        {
            var manager = Configuration.Default.ImageFormatsManager;

            var value = result.Tokens.Single().Value;

            var format = manager.FindByName(value);
            if (format == null)
            {
                result.AddError($"Unknown format: {value}");
                return null;
            }

            return format.Name;
        }
    };

    private static readonly RootCommand RootCommand = new("PhiInfo CLI")
    {
        Options = { PackagesOption, ClassDataOption, ImageFormatOption },
        Subcommands =
        {
            HttpServer.Command,
            Export.Command,
            Tool.Command
        }
    };

    private static IReadAt CreateReadAt(string path)
    {
        if (path.StartsWith("http://") || path.StartsWith("https://")) return new HttpReadAt(path);

        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists)
            throw new FileNotFoundException($"Package file not found: {path}");
        return new MmapReadAt(path);
    }

    internal static PhiInfoContext GetContext(ParseResult parseResult)
    {
        var zips = parseResult.GetValue(PackagesOption)!;
        var classData = parseResult.GetValue(ClassDataOption)!;
        return new PhiInfoContext(new AndroidPackagesDataProvider(zips, classData.OpenRead()));
    }

    private static int Main(string[] args)
    {
        return RootCommand.Parse(args).Invoke();
    }
}