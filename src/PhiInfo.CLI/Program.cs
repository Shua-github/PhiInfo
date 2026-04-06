using System;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Reflection;
using PhiInfo.Processing.Type;
using PhiInfo.Core.Type;

namespace PhiInfo.CLI;

internal class Program
{
    internal static AppInfo GetAppInfo()
    {
        var version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "Unknown";
        return new AppInfo(version, "CLI");
    }

    private static int Main(string[] args)
    {
        Option<FileInfo[]> packagesOption = new("--package")
        {
            Aliases = { "-p" },
            Description = """
                          Path to package files. A package file can be APK, main OBB, or patch OBB. 
                          If your copy of Phigros is downloaded from Google play require all of those 
                          or the first two files.
                          If your copy of Phigros is downloaded from TapTap, you only need to provide 
                          the APK file, since TapTap's APK already contains all the data.
                          """,
            Required = true
        };

        Option<FileInfo> classDataOption = new("--classdata")
        {
            Aliases = { "-cldb" },
            Description = "Path to the class data TPK file",
            DefaultValueFactory = _ => new FileInfo("./classdata.tpk")
        };
        
        Option<Language> langOption = new("--language")
        {
            Aliases = { "-l", "--lang" },
            Description = "Default language",
            DefaultValueFactory = _ => Language.Chinese
        };

        // 服务器
        Option<uint> portOption = new("--port")
        {
            Description = "Port number for the HTTP server",
            DefaultValueFactory = _ => 41669
        };

        Option<string> hostOption = new("--host")
        {
            Description = "Host for the HTTP server",
            DefaultValueFactory = _ => "127.0.0.1"
        };

        // 本地
        Option<string> localOutputOption = new("--output")
        {
            Aliases = { "-o" },
            Description = "Output directory for local mode",
            DefaultValueFactory = _ => "./output"
        };

        // 服务器命令
        Command serverCommand = new("server", "Run HTTP server mode");
        serverCommand.Options.Add(portOption);
        serverCommand.Options.Add(hostOption);
        serverCommand.SetAction(parseResult =>
        {
            var packages = parseResult.GetValue(packagesOption)!;
            var classData = parseResult.GetValue(classDataOption)!;
            var port = parseResult.GetValue(portOption);
            var host = parseResult.GetValue(hostOption)!;
            var lang = parseResult.GetValue(langOption);
            if (!ValidateCommonOptions(packages, classData))
                return;
            HttpServer.RunServerMode(packages, classData, port, host, lang);
        });

        // 本地命令
        Command localCommand = new("local", "Run local extraction mode");
        localCommand.Options.Add(localOutputOption);
        localCommand.SetAction(parseResult =>
        {
            var packages = parseResult.GetValue(packagesOption)!;
            var classData = parseResult.GetValue(classDataOption)!;
            var output = parseResult.GetValue(localOutputOption)!;
            var lang = parseResult.GetValue(langOption)!;
            if (!ValidateCommonOptions(packages, classData))
                return;
            Local.RunLocalMode(packages, classData, output, lang);
        });

        // Root command
        RootCommand rootCommand = new("PhiInfo CLI");
        rootCommand.Options.Add(packagesOption);
        rootCommand.Options.Add(classDataOption);
        rootCommand.Options.Add(langOption);
        rootCommand.Add(serverCommand);
        rootCommand.Add(localCommand);

        return rootCommand.Parse(args).Invoke();
    }
    
    private static bool ValidateCommonOptions(FileInfo[] packages, FileInfo classDataFile)
    {
        var anyNotFound = packages.FirstOrDefault(p => !p.Exists);
        if (anyNotFound is not null)
        {
            Console.WriteLine($"Error: Package file not found: {anyNotFound.FullName}");
            return false;
        }

        if (classDataFile is not { Exists: true })
        {
            Console.WriteLine($"Error: Class data file not found: {classDataFile?.FullName ?? "<null>"}");
            return false;
        }

        return true;
    }
}