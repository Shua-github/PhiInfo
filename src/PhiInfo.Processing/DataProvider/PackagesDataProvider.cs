using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using PhiInfo.Core.Type;
using Shua.Zip;

namespace PhiInfo.Processing.DataProvider;

public class AndroidPackagesDataProvider(IEnumerable<ShuaZip> zips, Stream cldbStream) : IDataProvider
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
        var (zip, entry) = FindEntryInAllZips("assets/bin/Data/globalgamemanagers.assets");
        return EnsureSeekable(zip.OpenFileStream(entry));
    }

    public byte[] GetIl2CppBinary()
    {
        var (zip, entry) = FindEntryInAllZips("lib/arm64-v8a/libil2cpp.so");
        return zip.ReadFile(entry);
    }

    public byte[] GetGlobalMetadata()
    {
        var (zip, entry) = FindEntryInAllZips("assets/bin/Data/Managed/Metadata/global-metadata.dat");
        return zip.ReadFile(entry);
    }

    public Stream GetLevel0()
    {
        var (zip, entry) = FindEntryInAllZips("assets/bin/Data/level0");
        return EnsureSeekable(zip.OpenFileStream(entry));
    }

    public Stream GetLevel22()
    {
        var level22Parts = new List<(int index, string name, ShuaZip zip)>();

        foreach (var zip in zips)
        {
            var entries = zip.Eocd.FileEntries
                .Where(e => e.Name.StartsWith("assets/bin/Data/level22.split", StringComparison.Ordinal));

            foreach (var entry in entries)
            {
                var suffix = entry.Name["assets/bin/Data/level22.split".Length..];
                if (int.TryParse(suffix, out var index))
                    level22Parts.Add((index, entry.Name, zip));
            }
        }

        if (level22Parts.Count == 0)
            throw new FileNotFoundException("Required Unity assets missing from APK");

        level22Parts.Sort((a, b) => a.index.CompareTo(b.index));

        MemoryStream level22 = new();

        foreach (var (_, name, zip) in level22Parts)
        {
            var data = zip.ReadFileByName(name);
            level22.Write(data, 0, data.Length);
        }

        level22.Position = 0;
        return level22;
    }

    public Stream GetCatalog()
    {
        var (zip, entry) = FindEntryInAllZips("assets/aa/catalog.json");
        return EnsureSeekable(zip.OpenFileStream(entry));
    }

    public Stream GetBundle(string name)
    {
        var (zip, entry) = FindEntryInAllZips("assets/aa/Android/" + name);
        return EnsureSeekable(zip.OpenFileStream(entry));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    internal bool TryFindEntryInAllZips(
        string fileName,
        [NotNullWhen(true)] out ShuaZip? zip,
        [NotNullWhen(true)] out FileEntry? entry)
    {
        foreach (var item in zips)
        {
            entry = item.TryFindEntry(fileName);
            if (entry is not null)
            {
                zip = item;
                return true;
            }
        }

        zip = null;
        entry = null;
        return false;
    }

    internal (ShuaZip, FileEntry) FindEntryInAllZips(string fileName)
    {
        if (TryFindEntryInAllZips(fileName, out var zip, out var entry))
            return (zip, entry);

        throw new FileNotFoundException($"Required Unity asset '{fileName}' missing from provided packages.");
    }

    private static Stream EnsureSeekable(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
            return stream;
        }

        var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;

        stream.Dispose();
        return ms;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            cldbStream.Dispose();
            foreach (var item in zips) item.Dispose();
        }

        _disposed = true;
    }
}
