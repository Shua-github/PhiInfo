using System;
using System.IO;
using System.Linq;
using System.Threading;
using PhiInfo.Processing;
using PhiInfo.Core.Type;
using Shua.Zip;

namespace PhiInfo.CLI;

internal static class HttpServer
{
    public static void RunServerMode(FileInfo[] packages, FileInfo classDataFile, uint port, string host, Language lang)
    {
        using var exitEvent = new ManualResetEventSlim(false);

        using var server = PhiInfoHttpServer.FromAndroidPackagesPathAndCldb(
            packages.Select(p => new ShuaZip(new MmapReadAt(p.FullName))), File.OpenRead(classDataFile.FullName),
            Program.GetAppInfo(), port, host, lang);

        server.OnRequestError += (sender, ex) => { Console.WriteLine($"Server error: {ex}"); };

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

            exitEvent.Set();
        }
    }
}
