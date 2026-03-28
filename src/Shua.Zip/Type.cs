using System;
using System.IO;
using System.Text;

#pragma warning disable IDE0130
namespace Shua.Zip
#pragma warning restore IDE0130
{
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

        public static CompressionMethod FromUInt16(ushort value) => new CompressionMethod(value);
    }

    public sealed class FileEntry
    {
        public string Name { get; }
        public CompressionMethod CompressionMethod { get; }
        public ulong CompressedSize { get; }
        public ulong UncompressedSize { get; }
        public uint Crc32 { get; }
        public ulong LocalHeaderOffset { get; }

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

        public static bool TryReadFromCentralDirectory(byte[] data, ref int position, out FileEntry? entry)
        {
            entry = null;
            if (position + 4 > data.Length)
            {
                return false;
            }

            uint signature = Binary.ReadUint32Le(data, ref position);
            if (signature != 0x02014B50)
            {
                position -= 4;
                return false;
            }

            int entryStart = position - 4;
            _ = Binary.ReadUint16Le(data, ref position); // version made by
            _ = Binary.ReadUint16Le(data, ref position); // version needed
            _ = Binary.ReadUint16Le(data, ref position); // flags

            var compressionMethod = CompressionMethod.FromUInt16(Binary.ReadUint16Le(data, ref position));

            _ = Binary.ReadUint16Le(data, ref position); // mod time
            _ = Binary.ReadUint16Le(data, ref position); // mod date

            uint crc32 = Binary.ReadUint32Le(data, ref position);
            uint compressedSize32 = Binary.ReadUint32Le(data, ref position);
            uint uncompressedSize32 = Binary.ReadUint32Le(data, ref position);

            ushort filenameLen = Binary.ReadUint16Le(data, ref position);
            ushort extraLen = Binary.ReadUint16Le(data, ref position);
            ushort commentLen = Binary.ReadUint16Le(data, ref position);

            _ = Binary.ReadUint16Le(data, ref position); // disk number start
            _ = Binary.ReadUint16Le(data, ref position); // internal attrs
            _ = Binary.ReadUint32Le(data, ref position); // external attrs

            uint localHeaderOffset32 = Binary.ReadUint32Le(data, ref position);

            if (position + filenameLen > data.Length)
            {
                return false;
            }

            string name = Encoding.UTF8.GetString(data, position, filenameLen);
            position += filenameLen;

            int extraStart = position;
            int extraEnd = position + extraLen;
            int extraOffset = 0;
            int extraLength = 0;
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
            if (position > data.Length)
            {
                return false;
            }

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

            if (length <= 0)
            {
                return (compressedSize, uncompressedSize, localHeaderOffset);
            }

            int position = offset;
            int end = offset + length;
            while (position + 4 <= end)
            {
                ushort headerId = Binary.ReadUint16Le(data, ref position);
                ushort dataSize = Binary.ReadUint16Le(data, ref position);

                if (headerId == 0x0001)
                {
                    int dataStart = position;
                    if (compressedSize32 == 0xFFFFFFFF && position + 8 <= end)
                    {
                        compressedSize = Binary.ReadUint64Le(data, ref position);
                    }

                    if (uncompressedSize32 == 0xFFFFFFFF && position + 8 <= end)
                    {
                        uncompressedSize = Binary.ReadUint64Le(data, ref position);
                    }

                    if (localHeaderOffset32 == 0xFFFFFFFF && position + 8 <= end)
                    {
                        localHeaderOffset = Binary.ReadUint64Le(data, ref position);
                    }

                    position = Math.Min(dataStart + dataSize, end);
                }
                else
                {
                    position += dataSize;
                }

                if (dataSize == 0)
                {
                    break;
                }
            }

            return (compressedSize, uncompressedSize, localHeaderOffset);
        }
    }

    public sealed class EndOfCentralDirectory
    {
        public ulong CentralDirectorySize { get; }
        public ulong CentralDirectoryOffset { get; }
        public ushort CommentLength { get; }
        public bool UsesZip64 { get; }

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

        public static EndOfCentralDirectory FromEocd(
            IReadAt reader,
            long eocdOffset,
            byte[] data)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            if (data.Length < 22)
            {
                throw new InvalidOperationException("EOCD data too short");
            }

            int position = 0;
            uint signature = Binary.ReadUint32Le(data, ref position);
            if (signature != 0x06054B50)
            {
                throw new InvalidOperationException("Invalid EOCD signature");
            }

            _ = Binary.ReadUint16Le(data, ref position); // disk number
            _ = Binary.ReadUint16Le(data, ref position); // cd start disk
            ushort diskEntries = Binary.ReadUint16Le(data, ref position);
            ushort totalEntries = Binary.ReadUint16Le(data, ref position);

            uint cdSize32 = Binary.ReadUint32Le(data, ref position);
            uint cdOffset32 = Binary.ReadUint32Le(data, ref position);
            ushort commentLen = Binary.ReadUint16Le(data, ref position);

            bool usesZip64 =
                cdSize32 == 0xFFFFFFFF
                || cdOffset32 == 0xFFFFFFFF
                || diskEntries == 0xFFFF
                || totalEntries == 0xFFFF;

            if (!usesZip64)
            {
                return new EndOfCentralDirectory(cdSize32, cdOffset32, commentLen, false);
            }

            long locatorOffset = eocdOffset - 20;
            if (locatorOffset < 0)
            {
                throw new InvalidOperationException("Zip64 locator not found");
            }

            using var locatorStream = reader.OpenRead(locatorOffset, 20);
            byte[] locator = new byte[20];
            int readTotal = 0;
            while (readTotal < locator.Length)
            {
                int read = locatorStream.Read(locator, readTotal, locator.Length - readTotal);
                if (read <= 0)
                {
                    throw new EndOfStreamException("Unexpected end of stream");
                }

                readTotal += read;
            }

            if (locator[0] != 0x50
                || locator[1] != 0x4B
                || locator[2] != 0x06
                || locator[3] != 0x07)
            {
                throw new InvalidOperationException("Zip64 locator not found");
            }

            int pos = 4;
            _ = Binary.ReadUint32Le(locator, ref pos); // disk number with zip64 eocd
            ulong zip64EocdOffset = Binary.ReadUint64Le(locator, ref pos);
            _ = Binary.ReadUint32Le(locator, ref pos); // total disks

            if (zip64EocdOffset > long.MaxValue)
            {
                throw new InvalidOperationException("Zip64 EOCD offset too large");
            }

            using var zip64Stream = reader.OpenRead((long)zip64EocdOffset, 56);
            byte[] zip64Header = new byte[56];
            readTotal = 0;
            while (readTotal < zip64Header.Length)
            {
                int read = zip64Stream.Read(zip64Header, readTotal, zip64Header.Length - readTotal);
                if (read <= 0)
                {
                    throw new EndOfStreamException("Unexpected end of stream");
                }

                readTotal += read;
            }

            return FromZip64Bytes(zip64Header);
        }

        public static EndOfCentralDirectory FromZip64Bytes(byte[] data)
        {
            if (data.Length < 56)
            {
                throw new InvalidOperationException("Zip64 EOCD data too short");
            }

            int position = 0;
            uint signature = Binary.ReadUint32Le(data, ref position);
            if (signature != 0x06064B50)
            {
                throw new InvalidOperationException("Invalid Zip64 EOCD signature");
            }

            _ = Binary.ReadUint64Le(data, ref position); // size of record
            _ = Binary.ReadUint16Le(data, ref position); // version made by
            _ = Binary.ReadUint16Le(data, ref position); // version needed
            _ = Binary.ReadUint32Le(data, ref position); // disk number
            _ = Binary.ReadUint32Le(data, ref position); // cd start disk
            _ = Binary.ReadUint64Le(data, ref position); // total entries on disk
            _ = Binary.ReadUint64Le(data, ref position); // total entries

            ulong cdSize = Binary.ReadUint64Le(data, ref position);
            ulong cdOffset = Binary.ReadUint64Le(data, ref position);

            return new EndOfCentralDirectory(cdSize, cdOffset, 0, true);
        }
    }

    internal static class Binary
    {
        public static ushort ReadUint16Le(byte[] data, ref int offset)
        {
            if (offset + 2 > data.Length)
            {
                throw new InvalidOperationException("Unexpected end of data");
            }

            ushort value = (ushort)(data[offset] | (data[offset + 1] << 8));
            offset += 2;
            return value;
        }

        public static uint ReadUint32Le(byte[] data, ref int offset)
        {
            if (offset + 4 > data.Length)
            {
                throw new InvalidOperationException("Unexpected end of data");
            }

            uint value =
                (uint)(data[offset]
                       | (data[offset + 1] << 8)
                       | (data[offset + 2] << 16)
                       | (data[offset + 3] << 24));
            offset += 4;
            return value;
        }

        public static ulong ReadUint64Le(byte[] data, ref int offset)
        {
            if (offset + 8 > data.Length)
            {
                throw new InvalidOperationException("Unexpected end of data");
            }

            ulong value =
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
}