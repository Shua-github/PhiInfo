using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhiInfo.Core
{
    internal sealed class ByteReader(byte[] data)
    {
        private readonly byte[] _data = data;
        private int _pos;

        public int ReadInt()
        {
            int value = BitConverter.ToInt32(_data, _pos);
            _pos += 4;
            return value;
        }
    }

    public enum CatalogKeyType : byte
    {
        Utf8String = 0,
        UnicodeString = 1,
        Byte = 4
    }

    public readonly struct CatalogKey
    {
        public CatalogKeyType Type { get; }
        public string? StringValue { get; }
        public byte? ByteValue { get; }

        private CatalogKey(CatalogKeyType type, string? stringValue, byte? byteValue)
        {
            Type = type;
            StringValue = stringValue;
            ByteValue = byteValue;
        }

        public static CatalogKey FromString(CatalogKeyType type, string value)
            => new(type, value, null);

        public static CatalogKey FromByte(byte value)
            => new(CatalogKeyType.Byte, null, value);

        public override string ToString()
            => StringValue ?? ByteValue?.ToString() ?? string.Empty;
    }

    public readonly struct CatalogValue
    {
        public bool IsReference { get; }
        public ushort RawValue { get; }
        public CatalogKey? ResolvedKey { get; }

        private CatalogValue(bool isReference, ushort rawValue, CatalogKey? resolvedKey)
        {
            IsReference = isReference;
            RawValue = rawValue;
            ResolvedKey = resolvedKey;
        }

        public static CatalogValue FromRaw(ushort value)
            => new(true, value, null);

        public static CatalogValue FromResolved(CatalogKey key)
            => new(false, 0, key);
    }

    public sealed class CatalogEntry(CatalogKey key, CatalogValue value)
    {
        public CatalogKey Key { get; } = key;
        public CatalogValue Value { get; set; } = value;
    }


    [JsonSerializable(typeof(Type.Catalog))]
    public partial class JsonContext : JsonSerializerContext
    {
    }

    public sealed class CatalogParser
    {
        private readonly List<CatalogEntry> _entries;

        public CatalogParser(
            byte[] keyData,
            byte[] bucketData,
            byte[] entryData)
        {
            _entries = Parse(keyData, bucketData, entryData);
        }

        public CatalogParser(Stream json)
        {
            var data = JsonSerializer.Deserialize(json, JsonContext.Default.Catalog);

            _entries = Parse(
                Convert.FromBase64String(data.m_KeyDataString),
                Convert.FromBase64String(data.m_BucketDataString),
                Convert.FromBase64String(data.m_EntryDataString));
        }

        public IReadOnlyList<CatalogEntry> GetAll()
            => _entries;

        public CatalogValue? Get(CatalogKey key)
        {
            foreach (var entry in _entries)
            {
                if (KeysEqual(entry.Key, key))
                {
                    return entry.Value;
                }
            }

            return null;
        }

        public CatalogValue? Get(string key)
        {
            foreach (var entry in _entries)
            {
                if ((entry.Key.Type == CatalogKeyType.Utf8String ||
                     entry.Key.Type == CatalogKeyType.UnicodeString) &&
                    entry.Key.StringValue == key)
                {
                    return entry.Value;
                }
            }

            return null;
        }

        private static bool KeysEqual(CatalogKey a, CatalogKey b)
        {
            if (a.Type != b.Type)
                return false;

            return a.Type switch
            {
                CatalogKeyType.Utf8String or CatalogKeyType.UnicodeString =>
                    a.StringValue == b.StringValue,
                CatalogKeyType.Byte =>
                    a.ByteValue == b.ByteValue,
                _ => false
            };
        }

        private static List<CatalogEntry> Parse(
            byte[] keyData,
            byte[] bucketData,
            byte[] entryData)
        {
            var reader = new ByteReader(bucketData);
            var table = new List<CatalogEntry>();

            int bucketCount = reader.ReadInt();

            for (int i = 0; i < bucketCount; i++)
            {
                int keyPos = reader.ReadInt();
                var keyType = (CatalogKeyType)keyData[keyPos++];
                CatalogKey key;

                switch (keyType)
                {
                    case CatalogKeyType.Utf8String:
                    case CatalogKeyType.UnicodeString:
                        int length = BitConverter.ToInt32(keyData, keyPos);
                        keyPos += 4;

                        Encoding encoding =
                            keyType == CatalogKeyType.UnicodeString
                                ? Encoding.Unicode
                                : Encoding.UTF8;

                        key = CatalogKey.FromString(
                            keyType,
                            encoding.GetString(keyData, keyPos, length));
                        break;

                    case CatalogKeyType.Byte:
                        key = CatalogKey.FromByte(keyData[keyPos]);
                        break;

                    default:
                        throw new InvalidOperationException($"Unknown key type: {keyType}");
                }

                int entryCount = reader.ReadInt();
                int entryPos = reader.ReadInt();
                for (int j = 1; j < entryCount; j++)
                    reader.ReadInt();
                int entryStart = 4 + 28 * entryPos;
                ushort raw = (ushort)(entryData[entryStart + 8] ^ (entryData[entryStart + 9] << 8));
                var value = CatalogValue.FromRaw(raw);

                table.Add(new CatalogEntry(key, value));
            }

            ResolveReferences(table);
            return table;
        }

        private static void ResolveReferences(List<CatalogEntry> table)
        {
            for (int i = 0; i < table.Count; i++)
            {
                var value = table[i].Value;
                if (!value.IsReference)
                    continue;

                ushort index = value.RawValue;
                if (index == 65535 || index >= table.Count)
                    continue;

                table[i].Value = CatalogValue.FromResolved(table[index].Key);
            }
        }
    }
}