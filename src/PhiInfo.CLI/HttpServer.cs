using System;
using System.CommandLine;
using System.Threading;
using PhiInfo.Core;
using PhiInfo.Processing;

namespace PhiInfo.CLI;

internal static class HttpServer
{
    private static readonly Option<uint> PortOption = new("--port")
    {
        Description = "Port number for the HTTP server",
        DefaultValueFactory = _ => 41669
    };

    private static readonly Option<string> HostOption = new("--host")
    {
        Description = "Host for the HTTP server",
        DefaultValueFactory = _ => "127.0.0.1"
    };

    public static readonly Command Command = new("server", "Run HTTP server mode")
    {
        Options =
        {
            PortOption,
            HostOption
        },
        Action = new CommandLineAction(HandleCommand)
    };

    private static int HandleCommand(ParseResult parseResult)
    {
        var port = parseResult.GetValue(PortOption);
        var host = parseResult.GetValue(HostOption)!;

        RunServerMode(Program.GetContext(parseResult), port, host);
        return 0;
    }

    private static void RunServerMode(PhiInfoContext context, uint port, string host)
    {
        using var exitEvent = new ManualResetEventSlim(false);

        using var server = new PhiInfoHttpServer(context, port, host);

        server.OnRequestError += (_, ex) => { Console.WriteLine($"Server error: {ex}"); };

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