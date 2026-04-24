using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.Unicode;
using System.Threading.Tasks;
using PhiInfo.Core;
using PhiInfo.Core.Asset;
using PhiInfo.Processing.Type;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace PhiInfo.Processing;

public class PhiInfoExport(PhiInfoContext context, string apiType, IImageFormat? imageFormat = null)
{
    private const string ApiVersion = "1.0";

    private static readonly ImageFormatManager Manager = Configuration.Default.ImageFormatsManager;

    private static readonly JsonContext JsonContext = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    });

    private readonly Suffix _suffix = new((imageFormat ?? JpegFormat.Instance).FileExtensions.First(), "txt", "ogg");

    public void Export(IOutputWriter outputWriter)
    {
        ExportInfo(outputWriter);
        ExportSystem(outputWriter);
        ExportAssets(outputWriter);
    }

    private void ExportInfo(IOutputWriter outputWriter)
    {
        WriteJson("/info/songs.json", context.Info.ExtractSongs(), JsonContext.ListSongInfo, outputWriter);
        WriteJson("/info/collection.json", context.Info.ExtractCollection(), JsonContext.ListFolder, outputWriter);
        WriteJson("/info/avatars.json", context.Info.ExtractAvatars(), JsonContext.ListAvatar, outputWriter);
        WriteJson("/info/tips.json", context.Info.ExtractTips(), JsonContext.DictionaryLanguageListString,
            outputWriter);
        WriteJson("/info/chapters.json", context.Info.ExtractChapters(), JsonContext.ListChapterInfo, outputWriter);
        WriteJson("/info/version.json", context.Field.GetPhiVersion(), JsonContext.PhiVersion, outputWriter);
    }

    private void ExportSystem(IOutputWriter outputWriter)
    {
        WriteJson(
            "/api_info.json",
            new ApiInfo(ApiVersion, apiType, _suffix),
            JsonContext.ApiInfo,
            outputWriter);
    }

    private void ExportAssets(IOutputWriter outputWriter)
    {
        var filteredAssets = context.Asset.Catalog
            .Where(asset =>
                asset.Key.EndsWith(".json") ||
                asset.Key.EndsWith(".wav") ||
                asset.Key.EndsWith(".jpg") ||
                asset.Key.StartsWith("avatar.")
            )
            .ToArray();

        WriteJson(
            "/asset/metadata.json",
            filteredAssets.Select(a => a.Key).ToArray(),
            JsonContext.StringArray,
            outputWriter
        );

        Parallel.ForEach(filteredAssets, asset =>
        {
            if (asset.Key.EndsWith(".json"))
                WriteTextAsset($"/asset/{asset.Key}.{_suffix.text}", asset.Value, outputWriter);
            else if (asset.Key.EndsWith(".wav"))
                WriteMusicAsset($"/asset/{asset.Key}.{_suffix.music}", asset.Value, outputWriter);
            else if (asset.Key.EndsWith(".jpg") || asset.Key.StartsWith("avatar."))
                WriteImageAsset($"/asset/{asset.Key}.{_suffix.image}", asset.Value, outputWriter);
        });
    }

    private void WriteTextAsset(string path, string bundleName, IOutputWriter outputWriter)
    {
        using var textData = context.Asset.Get<UnityText>(bundleName);
        WriteBytes(path, "text/plain", Encoding.UTF8.GetBytes(textData.Content), outputWriter);
    }

    private void WriteMusicAsset(string path, string bundleName, IOutputWriter outputWriter)
    {
        var musicData = PhiInfoDecoders.DecoderMusic(context.Asset.Get<UnityMusic>(bundleName));
        WriteBytes(path, "audio/ogg", musicData, outputWriter);
    }

    private void WriteImageAsset(string path, string bundleName, IOutputWriter outputWriter)
    {
        var imageFormatInstance = imageFormat ?? JpegFormat.Instance;
        using var ms = new MemoryStream();
        using var image = PhiInfoDecoders.DecoderImage(context.Asset.Get<UnityImage>(bundleName));
        image.Save(ms, Manager.GetEncoder(imageFormatInstance));
        WriteBytes(path, imageFormatInstance.DefaultMimeType, ms.ToArray(), outputWriter);
    }

    private void WriteJson<T>(string path, T data, JsonTypeInfo<T> typeInfo, IOutputWriter outputWriter)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, typeInfo);
        WriteBytes(path, "application/json", bytes, outputWriter);
    }

    private static void WriteBytes(string path, string mime, byte[] data, IOutputWriter outputWriter)
    {
        using var output = outputWriter.Create(path.TrimStart('/'), mime);
        output.Write(data, 0, data.Length);
    }
}