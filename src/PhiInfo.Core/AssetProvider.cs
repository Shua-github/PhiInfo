using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using PhiInfo.Core.Type;

namespace PhiInfo.Core;

public class AssetProvider(CatalogProvider catalogProvider, IAssetDataProvider dataProvider)
{
    private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = stream.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of stream");

            totalRead += read;
        }
    }

    private static byte[] ReadRange(Stream stream, long offset, int size)
    {
        var buffer = new byte[size];
        var oldPos = stream.Position;

        try
        {
            stream.Seek(offset, SeekOrigin.Begin);
            ReadExactly(stream, buffer, 0, size);
        }
        finally
        {
            stream.Position = oldPos;
        }

        return buffer;
    }

    private MappedAssetBundle LoadBundle(string path)
    {
        var file = GetBundle(path);

        var reader = new AssetsFileReader(file);
        var bundleFile = new AssetBundleFile();
        bundleFile.Read(reader);

        if (bundleFile.DataIsCompressed)
            bundleFile = BundleHelper.UnpackBundle(bundleFile);

        bundleFile.GetFileRange(0, out var offset, out var size);

        var stream = new SegmentStream(bundleFile.DataReader.BaseStream, offset, size);

        var infoFile = new AssetsFile();
        infoFile.Read(stream);

        return new MappedAssetBundle(bundleFile, infoFile);
    }

    private AssetTypeValueField? FindAssetField(MappedAssetBundle bundle, AssetClassID type)
    {
        foreach (var info in bundle.InfoAssetFile.AssetInfos)
            if (info.TypeId == (int)type)
                return bundle.InfoAssetFile.GetBaseField(info);
        return null;
    }

    public Image GetImageRaw(string path)
    {
        using var bundle = LoadBundle(path);

        var field = FindAssetField(bundle, AssetClassID.Texture2D)
                    ?? throw new ArgumentException("No Texture2D found.", nameof(path));

        var width = field["m_Width"].AsUInt;
        var height = field["m_Height"].AsUInt;
        var format = field["m_TextureFormat"].AsUInt;

        var offset = field["m_StreamData"]["offset"].AsLong;
        var size = field["m_StreamData"]["size"].AsLong;

        bundle.BundleFile.GetFileRange(1, out var dataFileOffset, out _);

        var data = ReadRange(
            bundle.BundleFile.DataReader.BaseStream,
            dataFileOffset + offset,
            (int)size
        );

        return new Image(format, width, height, data);
    }

    public Music GetMusicRaw(string path)
    {
        using var bundle = LoadBundle(path);

        var field = FindAssetField(bundle, AssetClassID.AudioClip)
                    ?? throw new ArgumentException("No AudioClip found.", nameof(path));

        var offset = field["m_Resource"]["m_Offset"].AsLong;
        var size = field["m_Resource"]["m_Size"].AsLong;
        var length = field["m_Length"].AsFloat;

        bundle.BundleFile.GetFileRange(1, out var dataFileOffset, out _);

        var data = ReadRange(
            bundle.BundleFile.DataReader.BaseStream,
            dataFileOffset + offset,
            (int)size
        );

        return new Music(length, data);
    }

    public Text GetTextRaw(string path)
    {
        using var bundle = LoadBundle(path);

        var field = FindAssetField(bundle, AssetClassID.TextAsset)
                    ?? throw new ArgumentException("No TextAsset found.", nameof(path));

        return new Text(field["m_Script"].AsString);
    }

    private Stream GetBundle(string path)
    {
        var bundlePath = catalogProvider.Get(path);

        if (bundlePath is string str)
            return dataProvider.GetBundle(str);

        throw new ArgumentException("Asset not found in catalog.", nameof(path));
    }

    public List<string> List()
    {
        return catalogProvider.GetAll()
            .Select(v => v.Key)
            .OfType<string>()
            .ToList();
    }

    private readonly record struct MappedAssetBundle(
        AssetBundleFile BundleFile,
        AssetsFile InfoAssetFile) : IDisposable
    {
        public void Dispose()
        {
            BundleFile.Close();
            InfoAssetFile.Close();
        }
    }
}