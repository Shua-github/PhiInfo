using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.Unicode;
using PhiInfo.Core;
using PhiInfo.Core.Type;
using PhiInfo.Processing.Type;

namespace PhiInfo.Processing;

[JsonSerializable(typeof(List<SongInfo>))]
[JsonSerializable(typeof(List<Folder>))]
[JsonSerializable(typeof(List<Avatar>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<ChapterInfo>))]
[JsonSerializable(typeof(ServerInfo))]
[JsonSerializable(typeof(AllInfo))]
[JsonSerializable(typeof(Language))]
[JsonSerializable(typeof(List<Language>))]
[JsonSerializable(typeof(Dictionary<string,string>))]
public partial class JsonContext : JsonSerializerContext
{
}

public class PhiInfoRouter(PhiInfoContext context, AppInfo appInfo)
{
    private static readonly Response MissParam = new(
        400,
        "text/plain",
        "Missing parameter"u8.ToArray()
    );

    private readonly JsonContext _jsonContext = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    });

    public Response Handle(string path, Dictionary<string, string> query)
    {
        
        if (path == "/asset")
        {
            var dict = context.Catalog.GetAll()
                .Where(v => v.Key.IsString && v.Value != null && v.Value.Value.IsString)
                .ToDictionary(
                    v => v.Key.Str!,
                    v => v.Value!.Value.Str
                );

            var json = SerializeJson(dict, _jsonContext.DictionaryStringString);
            return new Response(200, "application/json", json);
        }
        if (path.StartsWith("/asset/"))
        {
            var segments = path["/asset/".Length..]
                .Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length <= 1)
                return MissParam;

            var type = segments[^1];
            var assetPath = string.Join('/', segments.Take(segments.Length - 1));

            return HandleAsset(assetPath, type);
        }
        switch (path)
        {
            case "/info/songs":
                var songs = SerializeJson(context.Info.ExtractSongInfo(), _jsonContext.ListSongInfo);
                return new Response(200, "application/json", songs);

            case "/info/collection":
                var collection = SerializeJson(context.Info.ExtractCollection(), _jsonContext.ListFolder);
                return new Response(200, "application/json", collection);

            case "/info/avatars":
                var avatars = SerializeJson(context.Info.ExtractAvatars(), _jsonContext.ListAvatar);
                return new Response(200, "application/json", avatars);

            case "/info/tips":
                var tips = SerializeJson(context.Info.ExtractTips(), _jsonContext.ListString);
                return new Response(200, "application/json", tips);

            case "/info/chapters":
                var chapters = SerializeJson(context.Info.ExtractChapters(), _jsonContext.ListChapterInfo);
                return new Response(200, "application/json", chapters);

            case "/info/all":
                var allData = SerializeJson(context.Info.ExtractAllInfo(), _jsonContext.AllInfo);
                return new Response(200, "application/json", allData);

            case "/info/version":
                var version = context.Info.GetPhiVersion().ToString();
                return new Response(200, "text/plain", Encoding.UTF8.GetBytes(version));

            case "/info/server":
                var serverInfo = GetServerInfo();
                var serverData = SerializeJson(serverInfo, _jsonContext.ServerInfo);
                return new Response(200, "application/json", serverData);

            case "/lang/state":
                var currentLang = context.Language.ToString();
                return new Response(200, "text/plain", Encoding.UTF8.GetBytes(currentLang));

            case "/lang/set":
                if (!query.TryGetValue("lang", out var langStr) || string.IsNullOrEmpty(langStr))
                    return MissParam;

                if (!Enum.TryParse<Language>(langStr, true, out var lang))
                    return new Response(400, "text/plain", "Invalid language"u8.ToArray());

                context.Language = lang;
                return new Response(204, null, null);

            case "/lang/list":
                var languages = Enum.GetValues<Language>().Select(l => l.ToString()).ToList();
                var langData = SerializeJson(languages, _jsonContext.ListString);
                return new Response(200, "application/json", langData);

            default:
                return new Response(404, "text/plain", "Not Found"u8.ToArray());
        }
    }

    private byte[] SerializeJson<T>(T data, JsonTypeInfo<T> typeInfo)
    {
        return JsonSerializer.SerializeToUtf8Bytes(data, typeInfo);
    }

    private ServerInfo GetServerInfo()
    {
        var rid = RuntimeInformation.RuntimeIdentifier;

        var version = typeof(PhiInfoRouter)
            .Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "Unknown";

        return new ServerInfo(version, rid, appInfo);
    }
    
    private Response HandleAsset(string name, string type)
    {
        switch (type.ToLowerInvariant())
        {
            case "text":
                var textData = context.Asset.GetTextRaw(name);
                return new Response(200, "text/plain", Encoding.UTF8.GetBytes(textData.content));

            case "music":
                var rawMusic = context.Asset.GetMusicRaw(name);
                var musicData = PhiInfoDecoders.DecoderMusic(rawMusic);
                return new Response(200, "audio/ogg", musicData);

            case "image":
                var bmpData = PhiInfoDecoders.DecoderImageToBmp(
                    context.Asset.GetImageRaw(name)
                );
                return new Response(200, "image/bmp", bmpData);

            default:
                return new Response(400, "text/plain", "Invalid asset type"u8.ToArray());
        }
    }
}