using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Shua.Zip;

public interface IReadAt : IDisposable
{
    long Size { get; }
    Stream OpenRead(long offset, int length);
}

public sealed class FileEntry
{
    private ushort _extraLen;
    private ushort _nameLen;
    public ulong CompressedSize;
    public ushort CompressionMethod;
    public uint Crc32;
    public ushort DosModDate;
    public ushort DosModTime;
    public ulong LocalHeaderOffset;
    public string Name;
    public ulong UncompressedSize;

    public FileEntry(BinaryReader reader)
    {
        if (reader.ReadUInt32() != 0x02014B50)
            throw new InvalidOperationException("Invalid central directory signature");

        _ = reader.ReadUInt16(); // version made by

        ParseGeneral(reader);
        var commentLen = reader.ReadUInt16();

        _ = reader.ReadUInt16(); // disk number start
        _ = reader.ReadUInt16(); // internal attrs
        _ = reader.ReadUInt32(); // external attrs

        LocalHeaderOffset = reader.ReadUInt32();

        var nameBytes = reader.ReadBytes(_nameLen);

        Name = Encoding.UTF8.GetString(nameBytes);

        ParseExtra(reader);

        _ = reader.ReadBytes(commentLen);
    }

    public DateTime ModTime => ParseDosDateTime(DosModTime, DosModDate);

    public ulong DataOffset => LocalHeaderOffset + 30 + _nameLen + _extraLen;

    private static DateTime ParseDosDateTime(ushort time, ushort date)
    {
        var second = (time & 0x1F) * 2;
        var minute = (time >> 5) & 0x3F;
        var hour = (time >> 11) & 0x1F;

        var day = date & 0x1F;
        var month = (date >> 5) & 0x0F;
        var year = ((date >> 9) & 0x7F) + 1980;

        return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
    }

    public void FromLocalFileHeader(IReadAt readAt)
    {
        if (LocalHeaderOffset > long.MaxValue)
            throw new InvalidOperationException("Local header offset too large");

        using var stream = readAt.OpenRead((long)LocalHeaderOffset, 30);
        using var headerReader = new BinaryReader(stream, Encoding.UTF8, false);

        if (headerReader.ReadUInt32() != 0x04034B50)
            throw new InvalidOperationException("Invalid local file header signature");

        ParseGeneral(headerReader);
        using var reader = new BinaryReader(readAt.OpenRead((long)LocalHeaderOffset + 30, _nameLen + _extraLen));
        Name = Encoding.UTF8.GetString(reader.ReadBytes(_nameLen));
        ParseExtra(reader);
    }

    private void ParseGeneral(BinaryReader reader)
    {
        _ = reader.ReadUInt16(); // version needed
        _ = reader.ReadUInt16(); // flags
        CompressionMethod = reader.ReadUInt16();

        DosModTime = reader.ReadUInt16();
        DosModDate = reader.ReadUInt16();

        Crc32 = reader.ReadUInt32();
        CompressedSize = reader.ReadUInt32();
        UncompressedSize = reader.ReadUInt32();

        _nameLen = reader.ReadUInt16();
        _extraLen = reader.ReadUInt16();
    }

    private void ParseExtra(BinaryReader reader)
    {
        int remaining = _extraLen;

        while (remaining >= 4)
        {
            var headerId = reader.ReadUInt16();
            var dataSize = reader.ReadUInt16();
            remaining -= 4;

            if (dataSize > remaining)
                throw new InvalidOperationException("Corrupt ZIP extra field");

            if (headerId == 0x0001) // ZIP64
            {
                UncompressedSize = reader.ReadUInt64();
                CompressedSize = reader.ReadUInt64();
                LocalHeaderOffset = reader.ReadUInt64();
                _ = reader.ReadUInt32(); // Number of the disk on which this file starts 
            }
            else
            {
                reader.ReadBytes(dataSize);
            }

            remaining -= dataSize;
        }
    }
}

public sealed class EndOfCentralDirectory
{
    private readonly IReadAt _reader;
    private ulong _centralDirectoryOffset;
    private ulong _centralDirectorySize;
    public bool Zip64;

    public EndOfCentralDirectory(IReadAt reader)
    {
        _reader = reader;
        ParseEocd();
        FileEntries = LoadCentralDirectory();
    }

    public List<FileEntry> FileEntries { get; private set; }

    private (byte[] Buffer, int Index) FindEocd()
    {
        var size = _reader.Size;
        var searchStart = Math.Max(0, size - (65535 + 22 + 20));
        var searchLength = (int)(size - searchStart);

        using var stream = _reader.OpenRead(searchStart, searchLength);
        var buffer = new byte[searchLength];
        stream.ReadExactly(buffer, 0, searchLength);

        for (var i = searchLength - 22; i >= 0; i--)
        {
            if (buffer[i] != 0x50 || buffer[i + 1] != 0x4B ||
                buffer[i + 2] != 0x05 || buffer[i + 3] != 0x06)
                continue;

            var commentLen = (ushort)(buffer[i + 20] | (buffer[i + 21] << 8));
            if (i + 22 + commentLen != searchLength) continue;

            return (buffer, i);
        }

        throw new InvalidOperationException("End of Central Directory not found");
    }

    private void ParseEocd()
    {
        var (searchBuffer, eocdIndex) = FindEocd();
        var eocd = searchBuffer.AsSpan(eocdIndex, 22);

        var signature = BinaryPrimitives.ReadUInt32LittleEndian(eocd);
        if (signature != 0x06054B50)
            throw new InvalidOperationException("Invalid EOCD signature");

        var diskEntries = BinaryPrimitives.ReadUInt16LittleEndian(eocd.Slice(8, 2));
        var totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(eocd.Slice(10, 2));
        _centralDirectorySize = BinaryPrimitives.ReadUInt32LittleEndian(eocd.Slice(12, 4));
        _centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(eocd.Slice(16, 4));

        Zip64 = _centralDirectorySize == 0xFFFFFFFF || _centralDirectoryOffset == 0xFFFFFFFF || diskEntries == 0xFFFF ||
                totalEntries == 0xFFFF;

        if (!Zip64) return;

        var locatorIndex = eocdIndex - 20;
        if (locatorIndex < 0) throw new InvalidOperationException("Zip64 locator is outside of buffered range");

        var locatorSpan = searchBuffer.AsSpan(locatorIndex, 20);

        if (BinaryPrimitives.ReadUInt32LittleEndian(locatorSpan) != 0x07064B50)
            throw new InvalidOperationException("Zip64 locator not found");

        var zip64EocdOffset = BinaryPrimitives.ReadUInt64LittleEndian(locatorSpan.Slice(8, 8));

        if (zip64EocdOffset > long.MaxValue)
            throw new InvalidOperationException("Zip64 EOCD offset too large");

        using var zip64Stream = _reader.OpenRead((long)zip64EocdOffset, 56);
        using var zip64Reader = new BinaryReader(zip64Stream, Encoding.UTF8, false);

        ParseZip64(zip64Reader);
    }

    private void ParseZip64(BinaryReader reader)
    {
        // Zip64 EOCD signature (fixed marker 0x06064B50)
        var signature = reader.ReadUInt32();
        if (signature != 0x06064B50)
            throw new InvalidOperationException("Invalid Zip64 EOCD signature");

        // size of zip64 end of central directory record (8 bytes)
        _ = reader.ReadUInt64();

        // version made by (2 bytes)
        _ = reader.ReadUInt16();

        // version needed to extract (2 bytes)
        _ = reader.ReadUInt16();

        // number of this disk (4 bytes)
        _ = reader.ReadUInt32();

        // number of the disk with the start of the central directory (4 bytes)
        _ = reader.ReadUInt32();

        // total number of entries in the central directory on this disk (8 bytes)
        _ = reader.ReadUInt64();

        // total number of entries in the central directory (8 bytes)
        _ = reader.ReadUInt64();

        // size of the central directory (8 bytes)
        _centralDirectorySize = reader.ReadUInt64();

        // offset of start of central directory (8 bytes)
        _centralDirectoryOffset = reader.ReadUInt64();
    }

    private List<FileEntry> LoadCentralDirectory()
    {
        if (_centralDirectorySize > int.MaxValue)
            throw new InvalidOperationException("Central directory too large");

        if (_centralDirectoryOffset > long.MaxValue)
            throw new InvalidOperationException("Central directory offset too large");

        var cdStream = _reader.OpenRead((long)_centralDirectoryOffset, (int)_centralDirectorySize);
        using var binaryReader = new BinaryReader(cdStream);

        var endPosition = cdStream.Position + cdStream.Length;
        var entries = new List<FileEntry>();

        while (binaryReader.BaseStream.Position + 4 <= endPosition) entries.Add(new FileEntry(binaryReader));

        return entries;
    }
}