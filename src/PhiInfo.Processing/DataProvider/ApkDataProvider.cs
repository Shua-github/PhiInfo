using System;
using System.Collections.Generic;
using System.IO;
using PhiInfo.Core.Type;
using Shua.Zip;

namespace PhiInfo.Processing.DataProvider;

public class ApkDataProvider(ShuaZip zip, Stream cldbStream) : IDataProvider
{
    private bool _disposed;

    public Stream GetCldb()
    {
        var ms = new MemoryStream();
        cldbStream.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }

    public Stream GetGlobalGameManagers()
    {
        return zip.OpenFileStreamByName("assets/bin/Data/globalgamemanagers.assets");
    }

    public byte[] GetIl2CppBinary()
    {
        return zip.ReadFileByName("lib/arm64-v8a/libil2cpp.so");
    }

    public byte[] GetGlobalMetadata()
    {
        return zip.ReadFileByName("assets/bin/Data/Managed/Metadata/global-metadata.dat");
    }

    public Stream GetLevel0()
    {
        return zip.OpenFileStreamByName("assets/bin/Data/level0");
    }

    public Stream GetLevel22()
    {
        var level22Parts = new List<(int index, string name)>();

        foreach (var entry in zip.FileEntries)
            if (entry.Name.StartsWith("assets/bin/Data/level22.split", StringComparison.Ordinal))
            {
                var suffix = entry.Name["assets/bin/Data/level22.split".Length..];
                if (int.TryParse(suffix, out var index))
                    level22Parts.Add((index, entry.Name));
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

    public Stream GetCatalog()
    {
        return zip.OpenFileStreamByName("assets/aa/catalog.json");
    }

    public Stream GetBundle(string name)
    {
        return zip.OpenFileStreamByName("assets/aa/Android/" + name);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            cldbStream.Dispose();
            zip.Dispose();
        }

        _disposed = true;
    }
}