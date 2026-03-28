using Fmod5Sharp;
using Fmod5Sharp.CodecRebuilders;
using PhiInfo.Core;
using PhiInfo.Core.Type;
using Shua.Zip;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;

namespace PhiInfo
{
    [JsonSerializable(typeof(List<SongInfo>))]
    [JsonSerializable(typeof(List<Folder>))]
    [JsonSerializable(typeof(List<Avatar>))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(List<ChapterInfo>))]
    [JsonSerializable(typeof(AllInfo))]
    public partial class JsonContext : JsonSerializerContext
    {
    }

    public class HttpServer : IDisposable
    {
        private readonly JsonContext _jsonContext = new(new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        });

        private readonly PhiInfoAsset _phiAsset;
        private readonly Core.PhiInfo _phiInfo;
        protected readonly ShuaZip Zip;
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private bool _disposed;

        private readonly Dictionary<string, Func<HttpListenerRequest, Task<(byte[] data, string contentType)>>>
            _routeHandlers;

        public HttpServer(string apkPath, Stream cldbStream)
        {
            var reader = new MmapReadAt(apkPath);
            Zip = new ShuaZip(reader);

            using var catalogStream = Zip.OpenFileStreamByName("assets/aa/catalog.json");
            var catalogParser = new CatalogParser(catalogStream);

            _phiAsset = new PhiInfoAsset(catalogParser,
                (bundleName) => { return Zip.OpenFileStreamByName("assets/aa/Android/" + bundleName); });

            _phiInfo = new Core.PhiInfo(
                Zip.OpenFileStreamByName("assets/bin/Data/globalgamemanagers.assets"),
                Zip.OpenFileStreamByName("assets/bin/Data/level0"),
                SetupLevel22(Zip),
                Zip.ReadFileByName("lib/arm64-v8a/libil2cpp.so"),
                Zip.ReadFileByName("assets/bin/Data/Managed/Metadata/global-metadata.dat"),
                cldbStream
            );

            const string jsonStream = "application/json";

            _routeHandlers = new()
            {
                ["/asset/text"] = async r => (GetAssetText(r.QueryString["path"]), "text/plain"),
                ["/asset/music"] = async r => (GetAssetMusic(r.QueryString["path"]), "audio/ogg"),
                ["/asset/image"] = async r => (GetAssetImage(r.QueryString["path"]), "application/octet-stream"),
                ["/info/songs"] = async _ =>
                    (SerializeJson(_phiInfo.ExtractSongInfo(), _jsonContext.ListSongInfo), jsonStream),
                ["/info/collection"] = async _ =>
                    (SerializeJson(_phiInfo.ExtractCollection(), _jsonContext.ListFolder), jsonStream),
                ["/info/avatars"] = async _ =>
                    (SerializeJson(_phiInfo.ExtractAvatars(), _jsonContext.ListAvatar), jsonStream),
                ["/info/tips"] =
                    async _ => (SerializeJson(_phiInfo.ExtractTips(), _jsonContext.ListString), jsonStream),
                ["/info/chapters"] = async _ =>
                    (SerializeJson(_phiInfo.ExtractChapters(), _jsonContext.ListChapterInfo), jsonStream),
                ["/info/all"] = async _ => (SerializeJson(_phiInfo.ExtractAll(), _jsonContext.AllInfo), jsonStream),
                ["/info/version"] = async _ =>
                    (Encoding.UTF8.GetBytes(_phiInfo.GetPhiVersion().ToString()), "text/plain")
            };
        }

        private async Task HandleRequest(HttpListenerContext context)
        {
            using var response = context.Response;
            try
            {
                string path = context.Request.Url?.AbsolutePath.ToLower() ?? "";

                response.Headers.Add("Access-Control-Allow-Origin", "*");

                if (_routeHandlers.TryGetValue(path, out var handler))
                {
                    var (data, contentType) = await handler(context.Request);
                    response.ContentType = contentType;
                    response.ContentLength64 = data.Length;
                    await response.OutputStream.WriteAsync(data);
                }
                else
                {
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    byte[] msg = Encoding.UTF8.GetBytes("Endpoint not found");
                    await response.OutputStream.WriteAsync(msg);
                }
            }
            catch (Exception ex)
            {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                var errorBuffer = Encoding.UTF8.GetBytes($"Server Error: {ex.Message}");
                await response.OutputStream.WriteAsync(errorBuffer);
            }
        }

        private static byte[] SerializeJson<T>(T data, JsonTypeInfo<T> typeInfo)
        {
            return JsonSerializer.SerializeToUtf8Bytes(data, typeInfo);
        }

        private byte[] GetAssetText(string? path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path is empty");
            var textData = _phiAsset.GetText(path);
            return Encoding.UTF8.GetBytes(textData.content);
        }

        private byte[] GetAssetMusic(string? path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path is empty");
            var raw = _phiAsset.GetMusicRaw(path);
            var bank = FsbLoader.LoadFsbFromByteArray(raw.data);
            var music = FmodVorbisRebuilder.RebuildOggFile(bank.Samples[0]);
            return music;
        }

        private byte[] GetAssetImage(string? path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path is empty");
            return _phiAsset.GetImageRaw(path).WithHeader();
        }

        protected virtual void Log(string msg)
        {
            Console.WriteLine(msg);
        }

        private Stream SetupLevel22(ShuaZip zip)
        {
            var level22Parts = new List<(int index, string name)>();

            foreach (var entry in zip.FileEntries)
            {
                if (entry.Name.StartsWith("assets/bin/Data/level22.split", StringComparison.Ordinal))
                {
                    string suffix = entry.Name["assets/bin/Data/level22.split".Length..];
                    if (int.TryParse(suffix, out int index))
                        level22Parts.Add((index, entry.Name));
                }
            }

            if (level22Parts.Count == 0)
                throw new FileNotFoundException("Required Unity assets missing from APK");

            level22Parts.Sort((a, b) => a.index.CompareTo(b.index));
            MemoryStream level22 = new();
            foreach (var part in level22Parts)
            {
                var data = zip.ReadFileByName(part.name);
                level22.Write(data, 0, data.Length);
            }

            level22.Position = 0;

            return level22;
        }

        public async Task Start(uint port = 41669, string host = "localhost")
        {
            if (_listener?.IsListening == true) return;

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://{host}:{port}/");

            _cts = new CancellationTokenSource();
            _listener.Start();
            await Task.Run(() => ListenLoop(_cts.Token));
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener?.IsListening == true)
            {
                try
                {
                    var context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequest(context), token);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"[HttpServer] Accept Error: {ex.Message}");
                }
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            if (_listener?.IsListening == true)
            {
                _listener.Stop();
                _listener.Close();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _cts?.Dispose();
            Zip.Dispose();
            _phiInfo.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}