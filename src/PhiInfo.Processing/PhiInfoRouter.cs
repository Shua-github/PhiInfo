using System;
using System.Collections.Generic;
using System.IO;
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
using PhiInfo.Core.Asset;
using PhiInfo.Core.Type;
using PhiInfo.Processing.Type;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace PhiInfo.Processing;

[JsonSerializable(typeof(List<SongInfo>))]
[JsonSerializable(typeof(List<Folder>))]
[JsonSerializable(typeof(List<Avatar>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<ChapterInfo>))]
[JsonSerializable(typeof(PhiVersion))]
[JsonSerializable(typeof(ServerInfo))]
[JsonSerializable(typeof(AppInfo))]
[JsonSerializable(typeof(ApiInfo))]
[JsonSerializable(typeof(AllInfo))]
[JsonSerializable(typeof(Dictionary<Language, List<string>>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public partial class JsonContext : JsonSerializerContext
{
}

public class PhiInfoRouter(PhiInfoContext context, AppInfo appInfo, string apiType, IImageFormat? imageFormat = null)
{
    private const string ApiVersion = "1.0";

    private static readonly Response MissParam = new(
        400,
        "text/plain",
        "Missing parameter"u8.ToArray()
    );

    private static readonly ImageFormatManager Manager = Configuration.Default.ImageFormatsManager;

    private static readonly JsonContext JsonContext = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    });

    private readonly Suffix _suffix = new((imageFormat ?? JpegFormat.Instance).FileExtensions.First(), "txt", "ogg");

    public Response Handle(string path)
    {
        if (path.StartsWith("/asset/"))
        {
            if (path.Equals("/asset/metadata.json"))
            {
                var assets = SerializeJson(context.Catalog.GetStringKeys(), JsonContext.StringArray);
                return new Response(200, "application/json", assets);
            }

            var keyWithSuffix = path["/asset/".Length..];
            if (string.IsNullOrEmpty(keyWithSuffix))
                return MissParam;

            var dotIndex = keyWithSuffix.LastIndexOf('.');
            if (dotIndex <= 0 || dotIndex == keyWithSuffix.Length - 1)
                return MissParam;

            var keyStr = keyWithSuffix[..dotIndex];
            var suffix = keyWithSuffix[(dotIndex + 1)..];

            var type = suffix switch
            {
                var s when s.Equals(_suffix.text) => "text",
                var s when s.Equals(_suffix.music) => "music",
                var s when s.Equals(_suffix.image) => "image",
                _ => null
            };

            if (type is null)
                return new Response(400, "text/plain", "Invalid asset suffix"u8.ToArray());

            var name = context.Catalog.Get(keyStr);
            if (name?.IsString ?? false)
                return HandleAsset(name.Value.Str!, type);

            return new Response(404, "text/plain", "Key not found"u8.ToArray());
        }

        switch (path)
        {
            case "/info/songs.json":
                var songs = SerializeJson(context.Info.ExtractSongs(), JsonContext.ListSongInfo);
                return new Response(200, "application/json", songs);

            case "/info/collection.json":
                var collection = SerializeJson(context.Info.ExtractCollection(), JsonContext.ListFolder);
                return new Response(200, "application/json", collection);

            case "/info/avatars.json":
                var avatars = SerializeJson(context.Info.ExtractAvatars(), JsonContext.ListAvatar);
                return new Response(200, "application/json", avatars);

            case "/info/tips.json":
                var tips = SerializeJson(context.Info.ExtractTips(), JsonContext.DictionaryLanguageListString);
                return new Response(200, "application/json", tips);

            case "/info/chapters.json":
                var chapters = SerializeJson(context.Info.ExtractChapters(), JsonContext.ListChapterInfo);
                return new Response(200, "application/json", chapters);

#if DEBUG
            case "/info/all.json":
                var allData = SerializeJson(context.Info.ExtractAllInfo(), JsonContext.AllInfo);
                return new Response(200, "application/json", allData);
#endif

            case "/info/version.json":
                var version = SerializeJson(context.Info.GetPhiVersion(), JsonContext.PhiVersion);
                return new Response(200, "application/json", version);

            case "/api_info.json":
                var apiInfo =
                    SerializeJson(new ApiInfo(ApiVersion, apiType, _suffix),
                        JsonContext.ApiInfo);
                return new Response(200, "application/json", apiInfo);

            case "/_server":
                var serverInfo = GetServerInfo();
                var serverData = SerializeJson(serverInfo, JsonContext.ServerInfo);
                return new Response(200, "application/json", serverData);

            case "/_app":
                var appData = SerializeJson(appInfo, JsonContext.AppInfo);
                return new Response(200, "application/json", appData);

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

        return new ServerInfo(version, rid);
    }

    private Response HandleAsset(string name, string type)
    {
        try
        {
            switch (type)
            {
                case "text":
                {
                    using var textData = context.Bundle.Get<UnityText>(name);
                    return new Response(200, "text/plain", Encoding.UTF8.GetBytes(textData.Content));
                }

                case "music":
                    var musicData = PhiInfoDecoders.DecoderMusic(context.Bundle.Get<UnityMusic>(name));
                    return new Response(200, "audio/ogg", musicData);

                case "image":
                {
                    var imageFormatInstance = imageFormat ?? JpegFormat.Instance;
                    using var ms = new MemoryStream();
                    using var image = PhiInfoDecoders.DecoderImage(context.Bundle.Get<UnityImage>(name));
                    image.Save(ms, Manager.GetEncoder(imageFormatInstance));
                    return new Response(200, imageFormatInstance.DefaultMimeType, ms.ToArray());
                }

                default:
                    return new Response(400, "text/plain", "Invalid asset type"u8.ToArray());
            }
        }
        catch (Exception e)
        {
            return new Response(400, "text/plain", Encoding.UTF8.GetBytes(e.Message));
        }
    }
}