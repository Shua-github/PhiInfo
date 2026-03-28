using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Shua.Zip
{
    public sealed class ShuaZip : IDisposable
    {
        private readonly IReadAt _reader;
        private readonly long _size;
        public List<FileEntry> FileEntries { get; }
        private bool _disposed;

        public ShuaZip(IReadAt reader)
        {
            _reader = reader;
            _size = reader.Size;
            FileEntries = LoadCentralDirectory();
        }

        private List<FileEntry> LoadCentralDirectory()
        {
            var eocd = FindEocd();

            if (eocd.CentralDirectorySize > int.MaxValue)
            {
                throw new InvalidOperationException("Central directory too large");
            }

            var cdData = ReadAt((long)eocd.CentralDirectoryOffset, (int)eocd.CentralDirectorySize);

            return ParseCentralDirectory(cdData);
        }

        private List<FileEntry> ParseCentralDirectory(byte[] cdData)
        {
            var entries = new List<FileEntry>();
            int position = 0;

            while (position + 4 <= cdData.Length)
            {
                if (!FileEntry.TryReadFromCentralDirectory(cdData, ref position, out var entry))
                {
                    break;
                }

                if (entry != null)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        private EndOfCentralDirectory FindEocd()
        {
            long searchStart = Math.Max(0, _size - (65535 + 22));
            long searchEnd = _size;

            for (long offset = searchEnd - 1; offset >= searchStart; offset--)
            {
                if (searchEnd - offset < 22)
                {
                    continue;
                }

                var data = ReadAt(offset, 22);

                if (data.Length >= 4
                    && data[0] == 0x50
                    && data[1] == 0x4B
                    && data[2] == 0x05
                    && data[3] == 0x06)
                {
                    int commentLen = data[20] | (data[21] << 8);
                    if (offset + 22 + commentLen == _size)
                    {
                        return EndOfCentralDirectory.FromEocd(_reader, offset, data);
                    }
                }
            }

            throw new InvalidOperationException("End of Central Directory not found");
        }

        public byte[] ReadFile(FileEntry entry)
        {
            using var stream = OpenFileStream(entry);
            int capacity = 0;
            if (entry.CompressionMethod.IsStored)
            {
                capacity = (int)entry.CompressedSize;
            }
            else if (entry.CompressionMethod.IsDeflate)
            {
                capacity = (int)entry.UncompressedSize;
            }

            using var output = new MemoryStream(capacity);
            stream.CopyTo(output);
            return output.ToArray();
        }

        public Stream OpenFileStream(FileEntry entry)
        {
            if (entry.LocalHeaderOffset > long.MaxValue)
            {
                throw new InvalidOperationException("Local header offset too large");
            }

            var headerData = ReadAt((long)entry.LocalHeaderOffset, 30);

            if (headerData.Length < 30
                || headerData[0] != 0x50
                || headerData[1] != 0x4B
                || headerData[2] != 0x03
                || headerData[3] != 0x04)
            {
                throw new InvalidOperationException("Invalid local file header signature");
            }

            int filenameLen = headerData[26] | (headerData[27] << 8);
            int extraLen = headerData[28] | (headerData[29] << 8);

            long dataOffset = (long)entry.LocalHeaderOffset + 30L + filenameLen + extraLen;

            if (entry.CompressedSize > int.MaxValue)
            {
                throw new InvalidOperationException("Compressed size too large");
            }

            var rawStream = _reader.OpenRead(dataOffset, (int)entry.CompressedSize);

            if (entry.CompressionMethod.IsStored)
            {
                return rawStream;
            }

            if (entry.CompressionMethod.IsDeflate)
            {
                if (entry.UncompressedSize > int.MaxValue)
                    throw new InvalidOperationException("Uncompressed size too large");

                using var deflateStream = new DeflateStream(rawStream, CompressionMode.Decompress, leaveOpen: false);
                var memory = new MemoryStream((int)entry.UncompressedSize);
                deflateStream.CopyTo(memory);
                memory.Position = 0;
                return memory;
            }

            rawStream.Dispose();
            throw new InvalidOperationException($"Unsupported compression method: {entry.CompressionMethod.Value}");
        }

        private byte[] ReadAt(long offset, int length)
        {
            if (length == 0)
            {
                return [];
            }

            using var stream = _reader.OpenRead(offset, length);
            byte[] buffer = new byte[length];
            int readTotal = 0;

            while (readTotal < length)
            {
                int read = stream.Read(buffer, readTotal, length - readTotal);
                if (read <= 0)
                {
                    throw new EndOfStreamException("Unexpected end of stream");
                }

                readTotal += read;
            }

            return buffer;
        }

        public Stream OpenFileStreamByName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentNullException(nameof(fileName));

            var entry = FileEntries.Find(f => string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase)) ??
                        throw new FileNotFoundException($"File '{fileName}' not found in the archive.");

            return OpenFileStream(entry);
        }


        public byte[] ReadFileByName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentNullException(nameof(fileName));

            var entry = FileEntries.Find(f => string.Equals(f.Name, fileName, StringComparison.OrdinalIgnoreCase)) ??
                        throw new FileNotFoundException($"File '{fileName}' not found in the archive.");

            return ReadFile(entry);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _reader.Dispose();
        }
    }
}