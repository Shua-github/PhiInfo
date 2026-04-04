using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using PhiInfo.Core.Type;

namespace PhiInfo.Core;

public class PhiInfoAsset(CatalogParser catalogParser, Func<string, Stream> getBundleStreamFunc)
{
    private static byte[] ReadRangeAsBytes(Stream baseStream, long offset, int size)
    {
        var buffer = new byte[size];

        var oldPos = baseStream.Position;
        try
        {
            baseStream.Seek(offset, SeekOrigin.Begin);

            var readTotal = 0;
            while (readTotal < size)
            {
                var read = baseStream.Read(
                    buffer,
                    readTotal,
                    size - readTotal
                );

                if (read == 0)
                    throw new EndOfStreamException();

                readTotal += read;
            }
        }
        finally
        {
            baseStream.Position = oldPos;
        }

        return buffer;
    }

    private T ProcessAssetBundle<T>(string path, Func<AssetBundleFile, AssetsFile, T> processor)
    {
        var file = GetBundle(path);
        var reader = new AssetsFileReader(file);
        AssetBundleFile bun = new();
        bun.Read(reader);
        if (bun.DataIsCompressed) bun = BundleHelper.UnpackBundle(bun);

        bun.GetFileRange(0, out var offset, out var size);
        SegmentStream stream = new(bun.DataReader.BaseStream, offset, size);
        AssetsFile infoFile = new();
        infoFile.Read(new AssetsFileReader(stream));

        try
        {
            return processor(bun, infoFile);
        }
        finally
        {
            bun.Close();
            infoFile.Close();
        }
    }

    public Image GetImageRaw(string path)
    {
        return ProcessAssetBundle(path, (bun, infoFile) =>
        {
            foreach (var info in infoFile.AssetInfos)
                if (info.TypeId == (int)AssetClassID.Texture2D)
                {
                    var baseField = PhiInfo.GetBaseField(infoFile, info);
                    var height = baseField["m_Height"].AsUInt;
                    var width = baseField["m_Width"].AsUInt;
                    var format = baseField["m_TextureFormat"].AsUInt;
                    var dataOffset = baseField["m_StreamData"]["offset"].AsLong;
                    var dataSize = baseField["m_StreamData"]["size"].AsLong;
                    bun.GetFileRange(1, out var dataFileOffset, out _);
                    var data = ReadRangeAsBytes(bun.DataReader.BaseStream, dataFileOffset + dataOffset,
                        (int)dataSize);
                    var image = new Image(format, width, height, data);
                    return image;
                }

            throw new Exception("No Texture2D found in the asset bundle.");
        });
    }

    public Music GetMusicRaw(string path)
    {
        return ProcessAssetBundle(path, (bun, infoFile) =>
        {
            foreach (var info in infoFile.AssetInfos)
                if (info.TypeId == (int)AssetClassID.AudioClip)
                {
                    var baseField = PhiInfo.GetBaseField(infoFile, info);
                    var dataOffset = baseField["m_Resource"]["m_Offset"].AsLong;
                    var dataSize = baseField["m_Resource"]["m_Size"].AsLong;
                    var length = baseField["m_Length"].AsFloat;
                    bun.GetFileRange(1, out var dataFileOffset, out _);
                    var data = ReadRangeAsBytes(bun.DataReader.BaseStream, dataFileOffset + dataOffset,
                        (int)dataSize);
                    return new Music(length, data);
                }

            throw new Exception("No AudioClip found in the asset bundle.");
        });
    }

    public Text GetText(string path)
    {
        return ProcessAssetBundle(path, (_, infoFile) =>
        {
            foreach (var info in infoFile.AssetInfos)
                if (info.TypeId == (int)AssetClassID.TextAsset)
                {
                    var baseField = PhiInfo.GetBaseField(infoFile, info);
                    var text = baseField["m_Script"].AsString;
                    return new Text(text);
                }

            throw new Exception("No TextAsset found in the asset bundle.");
        });
    }

    private Stream GetBundle(string path)
    {
        var bundlePath = catalogParser.Get(path);
        if (bundlePath == null)
            throw new Exception($"Asset {path} not found in catalog.");
        if (bundlePath.Value.ResolvedKey == null)
            throw new Exception($"Asset {path} has no resolved bundle path.");
        if (bundlePath.Value.ResolvedKey.Value.StringValue == null)
            throw new Exception($"Asset {path} has invalid resolved bundle path.");

        return getBundleStreamFunc(bundlePath.Value.ResolvedKey.Value.StringValue);
    }

    public List<string> List()
    {
        return catalogParser.GetAll()
            .Where(e => e.Value.ResolvedKey != null && e.Key.StringValue is not null)
            .Select(v => v.Key.StringValue!)
            .ToList();
    }
}