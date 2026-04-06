using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PhiInfo.Processing;
using PhiInfo.Processing.DataProvider;
using PhiInfo.Core;
using PhiInfo.Core.Type;
using System.Text.Json;
using Shua.Zip;

namespace PhiInfo.CLI;

internal static class Local
{
    public static void RunLocalMode(FileInfo[] packages, FileInfo classDataFile, string localOutput, Language lang)
    {
        var dataProvider = new AndroidPackagesDataProvider(
            packages.Select(p => new ShuaZip(new MmapReadAt(p.FullName))), File.OpenRead(classDataFile.FullName));

        using var context = new PhiInfoContext(dataProvider, lang);

        Directory.CreateDirectory(localOutput);
        
        var allInfo = context.Info.ExtractAllInfo();
        var allInfoJson = JsonSerializer.Serialize(allInfo, PhiInfo.Processing.JsonContext.Default.AllInfo);
        File.WriteAllText(Path.Combine(localOutput, "all_info.json"), allInfoJson);
        
        var assetDir = Path.Combine(localOutput, "asset");
        Directory.CreateDirectory(assetDir);

        var assets = context.Catalog.GetAll()
            .Where(v => v.Key.IsString && v.Value != null && v.Value.Value.IsString)
            .Select(v => new { outputPath = v.Key.Str!, assetId = v.Value!.Value.Str! })
            .ToList();

        var tasks = assets.Select(asset => Task.Run(() => ExtractAsset(context, assetDir, asset.outputPath, asset.assetId))).ToArray();
        Task.WaitAll(tasks);

        Console.WriteLine("Local extraction completed.");
    }

    private static void ExtractAsset(PhiInfoContext context, string assetDir, string outputPath, string assetId)
    {
        var fullOutputPath = Path.Combine(assetDir, outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);

        if (outputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            var textData = context.Asset.GetTextRaw(assetId);
            File.WriteAllText(fullOutputPath, textData.content);
        }
        else if (outputPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            var rawMusic = context.Asset.GetMusicRaw(assetId);
            var musicData = PhiInfoDecoders.DecoderMusic(rawMusic);
            File.WriteAllBytes(fullOutputPath, musicData);
        }
        else if (outputPath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
        {
            var bmpData = PhiInfoDecoders.DecoderImageToBmp(context.Asset.GetImageRaw(assetId));
            var bmpOutputPath = fullOutputPath + ".bmp";
            File.WriteAllBytes(bmpOutputPath, bmpData);
        }
        else if (outputPath.StartsWith("avatar.", StringComparison.OrdinalIgnoreCase))
        {
            var bmpData = PhiInfoDecoders.DecoderImageToBmp(context.Asset.GetImageRaw(assetId));
            var bmpOutputPath = fullOutputPath + ".bmp";
            File.WriteAllBytes(bmpOutputPath, bmpData);
        }
    }
}
