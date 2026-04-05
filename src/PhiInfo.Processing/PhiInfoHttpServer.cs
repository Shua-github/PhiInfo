using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using PhiInfo.Core;
using PhiInfo.Core.Type;
using PhiInfo.Processing.DataProvider;
using PhiInfo.Processing.Type;
using Shua.Zip;

namespace PhiInfo.Processing;

public class PhiInfoHttpServer : IDisposable
{
    private readonly SemaphoreSlim _concurrencySemaphore = new(100);
    private readonly PhiInfoContext _context;
    private readonly CancellationTokenSource _cts = new();
    private readonly HttpListener _listener = new();
    private readonly PhiInfoRouter _router;
    private bool _disposed;
    private readonly Task? _listenerTask;

    public PhiInfoHttpServer(IDataProvider dataProvider, AppInfo appInfo, uint port = 41669, string host = "127.0.0.1",
        Language language = Language.Chinese)
    {
        _context = new PhiInfoContext(dataProvider, language);
        _router = new PhiInfoRouter(_context, appInfo);

        _listener.Prefixes.Add($"http://{host}:{port}/");
        _listener.IgnoreWriteExceptions = true;
        _listener.Start();
        _listenerTask = ListenLoopAsync(_cts.Token);
    }

    public bool IsRunning => _listener.IsListening;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts.Cancel();
        try
        {
            _listener.Stop();
            _listenerTask?.Wait(1000);
        }
        catch
        {
            // 忽略停止时的异常
        }
        finally
        {
            _listener.Close();
        }

        _context.Dispose();
        _cts.Dispose();
        _concurrencySemaphore.Dispose();
        GC.SuppressFinalize(this);
    }

    public event EventHandler<Exception>? OnRequestError;

    public static PhiInfoHttpServer FromApkPathAndCldb(string apkPath, Stream cldbStream, AppInfo appInfo,
        uint port = 41669, string host = "127.0.0.1", Language language = Language.Chinese)
    {
        return new PhiInfoHttpServer(new ApkDataProvider(new ShuaZip(new MmapReadAt(apkPath)), cldbStream), appInfo,
            port, host, language);
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
            try
            {
                var context = await _listener.GetContextAsync().ConfigureAwait(false);
                await _concurrencySemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                _ = ProcessRequestAsync(context, cancellationToken)
                    .ContinueWith(t =>
                    {
                        if (t.Exception?.InnerException != null)
                            OnRequestError?.Invoke(this, t.Exception.InnerException);
                        _concurrencySemaphore.Release();
                    }, TaskContinuationOptions.ExecuteSynchronously).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException
                                           or ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnRequestError?.Invoke(this, ex);
            }
    }

    private async Task ProcessRequestAsync(HttpListenerContext httpContext, CancellationToken cancellationToken)
    {
        using var responseObj = httpContext.Response;
        AddCorsHeaders(responseObj);

        if (httpContext.Request.HttpMethod.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            responseObj.StatusCode = 200;
            return;
        }

        var requestUrl = httpContext.Request.Url;
        var query = ParseQueryString(requestUrl?.Query ?? string.Empty);

        var result = _router.Handle(requestUrl?.AbsolutePath ?? "/", query);

        responseObj.StatusCode = result.code;
        responseObj.ContentType = result.mime;

        if (result.data?.Length > 0)
        {
            responseObj.ContentLength64 = result.data.LongLength;
            await responseObj.OutputStream.WriteAsync(result.data, 0, result.data.Length, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return result;
        var trimmed = query.TrimStart('?');
        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = part.Split('=', 2);
            var name = Uri.UnescapeDataString(segment[0]);
            var value = segment.Length > 1 ? Uri.UnescapeDataString(segment[1]) : string.Empty;
            result[name] = value;
        }

        return result;
    }

    private static void AddCorsHeaders(HttpListenerResponse response)
    {
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");
    }
}