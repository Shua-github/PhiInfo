using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Shua.Zip;

public delegate Stream DecompressionHandler(IReadAt reader, long dataOffset, int compressedSize, int uncompressedSize);

public sealed class ShuaZip : IDisposable
{
    private readonly Dictionary<ushort, DecompressionHandler> _decompressionHandlers;
    private readonly Dictionary<string, FileEntry> _entryIndex = new();
    private readonly IReadAt _reader;
    private bool _disposed;

    public ShuaZip(IReadAt reader)
    {
        _reader = reader;
        _decompressionHandlers = CreateDefaultHandlers();
        Eocd = new EndOfCentralDirectory(reader);
        foreach (var entry in Eocd.FileEntries) _entryIndex[entry.Name] = entry;
    }

    public EndOfCentralDirectory Eocd { get; }

    public List<FileEntry> FileEntries => Eocd.FileEntries;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reader.Dispose();
    }

    public void RegisterDecompression(ushort method, DecompressionHandler handler)
    {
        _decompressionHandlers[method] = handler;
    }

    private static Dictionary<ushort, DecompressionHandler> CreateDefaultHandlers()
    {
        return new Dictionary<ushort, DecompressionHandler>
        {
            {
                0, (reader, offset, compressedSize, uncompressedSize) =>
                    reader.OpenRead(offset, compressedSize)
            },
            {
                8,
                (reader, offset, compressedSize, uncompressedSize) =>
                {
                    return new DeflateStream(reader.OpenRead(offset, compressedSize), CompressionMode.Decompress,
                        false);
                }
            }
        };
    }

    public byte[] ReadFile(FileEntry entry, bool strictMode = true)
    {
        using var stream = OpenFileStream(entry, strictMode);
        int capacity;
        if (entry.CompressionMethod == 0)
            capacity = (int)entry.CompressedSize;
        else
            capacity = (int)entry.UncompressedSize;

        var buffer = new byte[capacity];
        stream.ReadExactly(buffer, 0, buffer.Length);
        return buffer;
    }

    public Stream OpenFileStream(FileEntry entry, bool strictMode = true)
    {
        if (strictMode)
            entry.FromLocalFileHeader(_reader);

        if (entry.CompressedSize > int.MaxValue) throw new InvalidOperationException("Compressed size too large");

        var compressionMethod = entry.CompressionMethod;

        if (!_decompressionHandlers.TryGetValue(compressionMethod, out var handler))
            throw new InvalidOperationException($"Unsupported compression method: {compressionMethod}");

        if (entry.UncompressedSize > int.MaxValue)
            throw new InvalidOperationException("Uncompressed size too large");

        return handler(_reader, (long)entry.DataOffset, (int)entry.CompressedSize, (int)entry.UncompressedSize);
    }

    public byte[] ReadFileByName(string fileName, bool strictMode = true)
    {
        var entry = TryFindEntry(fileName) ??
                    throw new FileNotFoundException($"File '{fileName}' not found in the archive.");

        return ReadFile(entry, strictMode);
    }

    public Stream OpenFileStreamByName(string fileName, bool strictMode = true)
    {
        var entry = TryFindEntry(fileName) ??
                    throw new FileNotFoundException($"File '{fileName}' not found in the archive.");

        return OpenFileStream(entry, strictMode);
    }

    public FileEntry? TryFindEntry(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            throw new ArgumentNullException(nameof(fileName));

        return _entryIndex.TryGetValue(fileName, out var entry) ? entry : null;
    }
}