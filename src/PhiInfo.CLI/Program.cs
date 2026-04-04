using System;
using System.CommandLine;
using System.IO;
using System.Reflection;
using System.Threading;
using global.PhiInfo.HttpServer.Type;

namespace PhiInfo.CLI;

public class CliHttpServer(string apkPath, Stream cldbStream) : HttpServer(apkPath, cldbStream)
{
    protected override AppInfo GetAppInfo()
    {
        var version = typeof(CliHttpServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "Unknown";
        return new AppInfo(version, "CLI");
    }
}

internal class Program
{
    private static int Main(string[] args)
    {
        Option<FileInfo> apkOption = new("--apk")
        {
            Description = "Path to the APK file",
            Required = true
        };

        Option<FileInfo> classDataOption = new("--classdata")
        {
            Description = "Path to the class data TPK file",
            DefaultValueFactory = _ => new FileInfo("./classdata.tpk")
        };

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

        RootCommand rootCommand = new("PhiInfo HTTP Server CLI");
        rootCommand.Options.Add(apkOption);
        rootCommand.Options.Add(classDataOption);
        rootCommand.Options.Add(portOption);
        rootCommand.Options.Add(hostOption);

        using var exitEvent = new ManualResetEventSlim(false);

        rootCommand.SetAction(parseResult =>
        {
            var apkFile = parseResult.GetValue(apkOption);
            var classDataFile = parseResult.GetValue(classDataOption);
            var port = parseResult.GetValue(portOption);
            var host = parseResult.GetValue(hostOption);

            if (apkFile is not { Exists: true })
            {
                Console.WriteLine($"Error: APK file not found: {apkFile?.FullName ?? "<null>"}");
                return;
            }

            if (classDataFile is not { Exists: true })
            {
                Console.WriteLine($"Error: Class data file not found: {classDataFile?.FullName ?? "<null>"}");
                return;
            }

            if (host == null)
            {
                Console.WriteLine("Error: Host is null");
                return;
            }

            using var cldb = File.OpenRead(classDataFile.FullName);
            using var server = new CliHttpServer(apkFile.FullName, cldb);

            _ = server.Start(port, host);

            // 注册事件
            Console.CancelKeyPress += OnCancelKeyPress;

            Console.WriteLine("--------------------------------------------");
            Console.WriteLine($"Server is running on http://{host}:{port}/");
            Console.WriteLine("Press Ctrl+C to stop the server.");
            Console.WriteLine("--------------------------------------------");

            exitEvent.Wait();


            Console.WriteLine("[System] Server stopped successfully.");
            return;

            void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
            {
                e.Cancel = true;

                Console.WriteLine("\n[System] Shutdown signal received.");
                Console.WriteLine("[System] Stopping server...");

                server.Stop();
                exitEvent.Set();
            }
        });

        return rootCommand.Parse(args).Invoke();
    }
}