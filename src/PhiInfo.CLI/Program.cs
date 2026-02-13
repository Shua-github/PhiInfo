namespace PhiInfo.CLI;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using PhiInfo.Core;

[JsonSerializable(typeof(Core.Type.AllInfo))]
public partial class JsonContext : JsonSerializerContext
{
}


struct Files
{
    public Stream ggm;
    public Stream level0;
    public byte[] il2cppBytes;
    public byte[] metadataBytes;
    public Stream level22;
}

class Program
{
    static readonly string dir = "./output/";
    static void Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Usage: <apk_path> <format_switch>");
            return;
        }

        var files = SetupFiles(args[0]);
        var formatSwitch = args[1] == "true" || args[1] == "1";

        var options = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = formatSwitch,
        };

        var context = new JsonContext(options);

        var phiInfo = new PhiInfo(
            files.ggm,
            files.level0,
            files.level22,
            files.il2cppBytes,
            files.metadataBytes,
            File.OpenRead("classdata.tpk")
        );

        var allInfo = phiInfo.ExtractAll();
        var allInfoJson = JsonSerializer.Serialize(allInfo, context.AllInfo);
        Directory.CreateDirectory(dir);
        File.WriteAllText(dir + "all_info.json", allInfoJson);
    }

    static Files SetupFiles(string apkPath)
    {
        Stream? ggm = null;
        Stream? level0 = null;
        byte[]? il2cppBytes = null;
        byte[]? metadataBytes = null;
        List<(int index, byte[] data)> level22Parts = new List<(int, byte[])>();

        using (var apkFs = File.OpenRead(apkPath))
        using (var zip = new ZipArchive(apkFs, ZipArchiveMode.Read))
        {
            foreach (var entry in zip.Entries)
            {
                switch (entry.FullName)
                {
                    case "assets/bin/Data/globalgamemanagers.assets":
                        ggm = ExtractEntryToMemoryStream(entry);
                        break;
                    case "assets/bin/Data/level0":
                        level0 = ExtractEntryToMemoryStream(entry);
                        break;
                    case "lib/arm64-v8a/libil2cpp.so":
                        il2cppBytes = ExtractEntryToMemory(entry);
                        break;
                    case "assets/bin/Data/Managed/Metadata/global-metadata.dat":
                        metadataBytes = ExtractEntryToMemory(entry);
                        break;
                }
                if (entry.FullName.StartsWith("assets/bin/Data/level22.split"))
                {
                    string suffix = entry.FullName["assets/bin/Data/level22.split".Length..];
                    int index = int.Parse(suffix);
                    level22Parts.Add((index, ExtractEntryToMemory(entry)));
                }
            }
        }

        if (ggm == null || level0 == null || il2cppBytes == null || metadataBytes == null || level22Parts.Count == 0)
            throw new FileNotFoundException("Required Unity assets not found in APK");


        level22Parts.Sort((a, b) => a.index.CompareTo(b.index));

        Stream level22 = new MemoryStream();
        foreach (var part in level22Parts)
            level22.Write(part.data, 0, part.data.Length);

        level22.Position = 0;

        var files = new Files
        {
            ggm = ggm,
            level0 = level0,
            il2cppBytes = il2cppBytes,
            metadataBytes = metadataBytes,
            level22 = level22
        };

        return files;
    }

    static byte[] ExtractEntryToMemory(ZipArchiveEntry entry)
    {
        using (var ms = new MemoryStream())
        using (var entryStream = entry.Open())
        {
            entryStream.CopyTo(ms);
            return ms.ToArray();
        }
    }

    static Stream ExtractEntryToMemoryStream(ZipArchiveEntry entry)
    {
        var ms = new MemoryStream();
        using (var entryStream = entry.Open())
        {
            entryStream.CopyTo(ms);
        }
        ms.Position = 0;
        return ms;
    }
}
