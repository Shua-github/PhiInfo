using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using LibCpp2IL.Logging;
using PhiInfo.Core;
using PhiInfo.Core.Type;
using PhiInfo.Processing.DataProvider;
using Shua.Zip;
using Shua.Zip.ReadAt;

namespace PhiInfo.CLI;

public class QuietLogWriter : LogWriter
{
    public override void Error(string message) { }
    public override void Info(string message) { }
    public override void Verbose(string message) { }
    public override void Warn(string message) { }
}

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

    private static readonly Option<Language> LangOption = new("--language")
    {
        Aliases = { "-l", "--lang" },
        Description = "Default language",
        DefaultValueFactory = _ => Language.Chinese
    };

    private static readonly RootCommand RootCommand = new("PhiInfo CLI")
    {
        Options = { PackagesOption, ClassDataOption, LangOption },
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
        var lang = parseResult.GetValue(LangOption);
        return new PhiInfoContext(new AndroidPackagesDataProvider(zips, classData.OpenRead()), lang);
    }

    private static int Main(string[] args)
    {
        LibLogger.Writer = new QuietLogWriter();
        return RootCommand.Parse(args).Invoke();
    }
}
