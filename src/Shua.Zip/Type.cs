using System;
using System.IO;
using System.Text;

#pragma warning disable IDE0130
namespace Shua.Zip;
#pragma warning restore IDE0130

public interface IReadAt : IDisposable
{
    long Size { get; }
    Stream OpenRead(long offset, int length);
}

public readonly struct CompressionMethod(ushort value)
{
    public ushort Value { get; } = value;

    public bool IsStored => Value == 0;
    public bool IsDeflate => Value == 8;

    public static CompressionMethod FromUInt16(ushort value)
    {
        return new CompressionMethod(value);
    }
}

public sealed class FileEntry
{
    private FileEntry(
        string name,
        CompressionMethod compressionMethod,
        ulong compressedSize,
        ulong uncompressedSize,
        uint crc32,
        ulong localHeaderOffset)
    {
        Name = name;
        CompressionMethod = compressionMethod;
        CompressedSize = compressedSize;
        UncompressedSize = uncompressedSize;
        Crc32 = crc32;
        LocalHeaderOffset = localHeaderOffset;
    }

    public string Name { get; }
    public CompressionMethod CompressionMethod { get; }
    public ulong CompressedSize { get; }
    public ulong UncompressedSize { get; }
    public uint Crc32 { get; }
    public ulong LocalHeaderOffset { get; }

    public static bool TryReadFromCentralDirectory(byte[] data, ref int position, out FileEntry? entry)
    {
        entry = null;
        if (position + 4 > data.Length) return false;

        var signature = Binary.ReadUint32Le(data, ref position);
        if (signature != 0x02014B50)
        {
            position -= 4;
            return false;
        }

        var entryStart = position - 4;
        _ = Binary.ReadUint16Le(data, ref position); // version made by
        _ = Binary.ReadUint16Le(data, ref position); // version needed
        _ = Binary.ReadUint16Le(data, ref position); // flags

        var compressionMethod = CompressionMethod.FromUInt16(Binary.ReadUint16Le(data, ref position));

        _ = Binary.ReadUint16Le(data, ref position); // mod time
        _ = Binary.ReadUint16Le(data, ref position); // mod date

        var crc32 = Binary.ReadUint32Le(data, ref position);
        var compressedSize32 = Binary.ReadUint32Le(data, ref position);
        var uncompressedSize32 = Binary.ReadUint32Le(data, ref position);

        var filenameLen = Binary.ReadUint16Le(data, ref position);
        var extraLen = Binary.ReadUint16Le(data, ref position);
        var commentLen = Binary.ReadUint16Le(data, ref position);

        _ = Binary.ReadUint16Le(data, ref position); // disk number start
        _ = Binary.ReadUint16Le(data, ref position); // internal attrs
        _ = Binary.ReadUint32Le(data, ref position); // external attrs

        var localHeaderOffset32 = Binary.ReadUint32Le(data, ref position);

        if (position + filenameLen > data.Length) return false;

        var name = Encoding.UTF8.GetString(data, position, filenameLen);
        position += filenameLen;

        var extraStart = position;
        var extraEnd = position + extraLen;
        var extraOffset = 0;
        var extraLength = 0;
        if (extraStart >= 0 && extraEnd <= data.Length && extraLen > 0)
        {
            extraOffset = extraStart;
            extraLength = extraLen;
        }

        var (compressedSize, uncompressedSize, localHeaderOffset) = ParseZip64Extra(
            data,
            extraOffset,
            extraLength,
            compressedSize32,
            uncompressedSize32,
            localHeaderOffset32);

        position = entryStart + 46 + filenameLen + extraLen + commentLen;
        if (position > data.Length) return false;

        entry = new FileEntry(
            name,
            compressionMethod,
            compressedSize,
            uncompressedSize,
            crc32,
            localHeaderOffset);
        return true;
    }

    private static (ulong compressedSize, ulong uncompressedSize, ulong localHeaderOffset) ParseZip64Extra(
        byte[] data,
        int offset,
        int length,
        uint compressedSize32,
        uint uncompressedSize32,
        uint localHeaderOffset32)
    {
        ulong compressedSize = compressedSize32;
        ulong uncompressedSize = uncompressedSize32;
        ulong localHeaderOffset = localHeaderOffset32;

        if (length <= 0) return (compressedSize, uncompressedSize, localHeaderOffset);

        var position = offset;
        var end = offset + length;
        while (position + 4 <= end)
        {
            var headerId = Binary.ReadUint16Le(data, ref position);
            var dataSize = Binary.ReadUint16Le(data, ref position);

            if (headerId == 0x0001)
            {
                var dataStart = position;
                if (compressedSize32 == 0xFFFFFFFF && position + 8 <= end)
                    compressedSize = Binary.ReadUint64Le(data, ref position);

                if (uncompressedSize32 == 0xFFFFFFFF && position + 8 <= end)
                    uncompressedSize = Binary.ReadUint64Le(data, ref position);

                if (localHeaderOffset32 == 0xFFFFFFFF && position + 8 <= end)
                    localHeaderOffset = Binary.ReadUint64Le(data, ref position);

                position = Math.Min(dataStart + dataSize, end);
            }
            else
            {
                position += dataSize;
            }

            if (dataSize == 0) break;
        }

        return (compressedSize, uncompressedSize, localHeaderOffset);
    }
}

public sealed class EndOfCentralDirectory
{
    private EndOfCentralDirectory(
        ulong centralDirectorySize,
        ulong centralDirectoryOffset,
        ushort commentLength,
        bool usesZip64)
    {
        CentralDirectorySize = centralDirectorySize;
        CentralDirectoryOffset = centralDirectoryOffset;
        CommentLength = commentLength;
        UsesZip64 = usesZip64;
    }

    public ulong CentralDirectorySize { get; }
    public ulong CentralDirectoryOffset { get; }
    public ushort CommentLength { get; }
    public bool UsesZip64 { get; }

    public static EndOfCentralDirectory FromEocd(
        IReadAt reader,
        long eocdOffset,
        byte[] data)
    {
        if (reader == null) throw new ArgumentNullException(nameof(reader));

        if (data.Length < 22) throw new InvalidOperationException("EOCD data too short");

        var position = 0;
        var signature = Binary.ReadUint32Le(data, ref position);
        if (signature != 0x06054B50) throw new InvalidOperationException("Invalid EOCD signature");

        _ = Binary.ReadUint16Le(data, ref position); // disk number
        _ = Binary.ReadUint16Le(data, ref position); // cd start disk
        var diskEntries = Binary.ReadUint16Le(data, ref position);
        var totalEntries = Binary.ReadUint16Le(data, ref position);

        var cdSize32 = Binary.ReadUint32Le(data, ref position);
        var cdOffset32 = Binary.ReadUint32Le(data, ref position);
        var commentLen = Binary.ReadUint16Le(data, ref position);

        var usesZip64 =
            cdSize32 == 0xFFFFFFFF
            || cdOffset32 == 0xFFFFFFFF
            || diskEntries == 0xFFFF
            || totalEntries == 0xFFFF;

        if (!usesZip64) return new EndOfCentralDirectory(cdSize32, cdOffset32, commentLen, false);

        var locatorOffset = eocdOffset - 20;
        if (locatorOffset < 0) throw new InvalidOperationException("Zip64 locator not found");

        using var locatorStream = reader.OpenRead(locatorOffset, 20);
        var locator = new byte[20];
        var readTotal = 0;
        while (readTotal < locator.Length)
        {
            var read = locatorStream.Read(locator, readTotal, locator.Length - readTotal);
            if (read <= 0) throw new EndOfStreamException("Unexpected end of stream");

            readTotal += read;
        }

        if (locator[0] != 0x50
            || locator[1] != 0x4B
            || locator[2] != 0x06
            || locator[3] != 0x07)
            throw new InvalidOperationException("Zip64 locator not found");

        var pos = 4;
        _ = Binary.ReadUint32Le(locator, ref pos); // disk number with zip64 eocd
        var zip64EocdOffset = Binary.ReadUint64Le(locator, ref pos);
        _ = Binary.ReadUint32Le(locator, ref pos); // total disks

        if (zip64EocdOffset > long.MaxValue) throw new InvalidOperationException("Zip64 EOCD offset too large");

        using var zip64Stream = reader.OpenRead((long)zip64EocdOffset, 56);
        var zip64Header = new byte[56];
        readTotal = 0;
        while (readTotal < zip64Header.Length)
        {
            var read = zip64Stream.Read(zip64Header, readTotal, zip64Header.Length - readTotal);
            if (read <= 0) throw new EndOfStreamException("Unexpected end of stream");

            readTotal += read;
        }

        return FromZip64Bytes(zip64Header);
    }

    public static EndOfCentralDirectory FromZip64Bytes(byte[] data)
    {
        if (data.Length < 56) throw new InvalidOperationException("Zip64 EOCD data too short");

        var position = 0;
        var signature = Binary.ReadUint32Le(data, ref position);
        if (signature != 0x06064B50) throw new InvalidOperationException("Invalid Zip64 EOCD signature");

        _ = Binary.ReadUint64Le(data, ref position); // size of record
        _ = Binary.ReadUint16Le(data, ref position); // version made by
        _ = Binary.ReadUint16Le(data, ref position); // version needed
        _ = Binary.ReadUint32Le(data, ref position); // disk number
        _ = Binary.ReadUint32Le(data, ref position); // cd start disk
        _ = Binary.ReadUint64Le(data, ref position); // total entries on disk
        _ = Binary.ReadUint64Le(data, ref position); // total entries

        var cdSize = Binary.ReadUint64Le(data, ref position);
        var cdOffset = Binary.ReadUint64Le(data, ref position);

        return new EndOfCentralDirectory(cdSize, cdOffset, 0, true);
    }
}

internal static class Binary
{
    public static ushort ReadUint16Le(byte[] data, ref int offset)
    {
        if (offset + 2 > data.Length) throw new InvalidOperationException("Unexpected end of data");

        var value = (ushort)(data[offset] | (data[offset + 1] << 8));
        offset += 2;
        return value;
    }

    public static uint ReadUint32Le(byte[] data, ref int offset)
    {
        if (offset + 4 > data.Length) throw new InvalidOperationException("Unexpected end of data");

        var value =
            (uint)(data[offset]
                   | (data[offset + 1] << 8)
                   | (data[offset + 2] << 16)
                   | (data[offset + 3] << 24));
        offset += 4;
        return value;
    }

    public static ulong ReadUint64Le(byte[] data, ref int offset)
    {
        if (offset + 8 > data.Length) throw new InvalidOperationException("Unexpected end of data");

        var value =
            data[offset]
            | ((ulong)data[offset + 1] << 8)
            | ((ulong)data[offset + 2] << 16)
            | ((ulong)data[offset + 3] << 24)
            | ((ulong)data[offset + 4] << 32)
            | ((ulong)data[offset + 5] << 40)
            | ((ulong)data[offset + 6] << 48)
            | ((ulong)data[offset + 7] << 56);
        offset += 8;
        return value;
    }
}