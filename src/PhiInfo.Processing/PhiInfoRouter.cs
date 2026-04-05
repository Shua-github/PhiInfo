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
public partial class JsonContext : JsonSerializerContext
{
}

public class PhiInfoRouter(PhiInfoContext context, AppInfo appInfo)
{
    private static readonly Response MissPath = new(
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
        try
        {
            switch (path)
            {
                case "/asset/text":
                    if (!query.TryGetValue("path", out var textPath) || string.IsNullOrEmpty(textPath))
                        return MissPath;

                    var textData = context.Asset.GetTextRaw(textPath);
                    return new Response(200, "text/plain", Encoding.UTF8.GetBytes(textData.content));

                case "/asset/music":
                    if (!query.TryGetValue("path", out var musicPath) || string.IsNullOrEmpty(musicPath))
                        return MissPath;

                    var rawMusic = context.Asset.GetMusicRaw(musicPath);
                    var musicData = PhiInfoDecoders.DecoderMusic(rawMusic);
                    return new Response(200, "audio/ogg", musicData);

                case "/asset/image":
                    if (!query.TryGetValue("path", out var imagePath) || string.IsNullOrEmpty(imagePath))
                        return MissPath;

                    var bmpData = PhiInfoDecoders.DecoderImageToBmp(
                        context.Asset.GetImageRaw(imagePath)
                    );
                    return new Response(200, "image/bmp", bmpData);

                case "/asset/list":
                    var assetList = SerializeJson(context.Asset.List(), _jsonContext.ListString);
                    return new Response(200, "application/json", assetList);

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
                    var allInfo = new AllInfo(
                        context.Info.GetPhiVersion(),
                        context.Info.ExtractSongInfo(),
                        context.Info.ExtractCollection(),
                        context.Info.ExtractAvatars(),
                        context.Info.ExtractTips(),
                        context.Info.ExtractChapters()
                    );

                    var allData = SerializeJson(allInfo, _jsonContext.AllInfo);
                    return new Response(200, "application/json", allData);

                case "/info/version":
                    var version = context.Info.GetPhiVersion().ToString();
                    return new Response(200, "text/plain", Encoding.UTF8.GetBytes(version));

                case "/info/server":
                    var serverInfo = GetServerInfo();
                    var serverData = SerializeJson(serverInfo, _jsonContext.ServerInfo);
                    return new Response(200, "application/json", serverData);

                case "/lang/get":
                    var currentLang = context.Language.ToString();
                    return new Response(200, "text/plain", Encoding.UTF8.GetBytes(currentLang));

                case "/lang/set":
                    if (!query.TryGetValue("lang", out var langStr) || string.IsNullOrEmpty(langStr))
                        return MissPath;

                    if (!Enum.TryParse<Language>(langStr, true, out var lang))
                        return new Response(400, "text/plain", "Invalid language"u8.ToArray());

                    context.Language = lang;
                    return new Response(200, "text/plain", "Language set"u8.ToArray());

                case "/lang/list":
                    var languages = Enum.GetValues<Language>().Select(l => l.ToString()).ToList();
                    var langData = SerializeJson(languages, _jsonContext.ListString);
                    return new Response(200, "application/json", langData);

                default:
                    return new Response(404, "text/plain", "Not Found"u8.ToArray());
            }
        }
        catch (Exception)
        {
            return new Response(500, "text/plain", "Internal Server Error"u8.ToArray());
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
}