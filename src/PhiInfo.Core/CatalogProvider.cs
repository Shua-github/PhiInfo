using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhiInfo.Core.Type;

namespace PhiInfo.Core;

public enum CatalogKeyType : byte
{
    Utf8String = 0,
    UnicodeString = 1,
    Byte = 4
}

[JsonSerializable(typeof(Catalog))]
public partial class JsonContext : JsonSerializerContext
{
}

public sealed class CatalogProvider
{
    private readonly ImmutableDictionary<object, object?> _entries;
    private readonly Dictionary<string, object?> _stringIndex;

    public CatalogProvider(ICatalogDataProvider dataProvider)
    {
        using var catalog = dataProvider.GetCatalog();

        var json = JsonSerializer.Deserialize(
                       catalog,
                       JsonContext.Default.Catalog)
                   ?? throw new InvalidOperationException();

        var keyData = Convert.FromBase64String(json.m_KeyDataString);
        var bucketData = Convert.FromBase64String(json.m_BucketDataString);
        var entryData = Convert.FromBase64String(json.m_EntryDataString);

        var dict = Parse(keyData, bucketData, entryData);

        _entries = dict.ToImmutableDictionary();

        _stringIndex = new Dictionary<string, object?>(dict.Count);

        foreach (var kv in dict)
        {
            if (kv.Key is byte)
                continue;

            var keyStr = kv.Key.ToString()!;

            if (!_stringIndex.ContainsKey(keyStr))
                _stringIndex[keyStr] = kv.Value;
        }
    }

    public IReadOnlyDictionary<object, object?> GetAll()
    {
        return _entries;
    }

    public bool TryGet(string key, out object? value)
    {
        return _stringIndex.TryGetValue(key, out value);
    }

    public bool TryGet(object key, out object? value)
    {
        return _entries.TryGetValue(key, out value);
    }

    public object? Get(string key)
    {
        return _stringIndex.TryGetValue(key, out var value) ? value : null;
    }

    private static Dictionary<object, object?> Parse(
        byte[] keyData,
        byte[] bucketData,
        byte[] entryData)
    {
        var reader = new BinaryReader(new MemoryStream(bucketData));
        var temp = new List<(object Key, ushort ValueIndex)>();

        var bucketCount = reader.ReadInt32();

        for (var i = 0; i < bucketCount; i++)
        {
            var keyPos = reader.ReadInt32();

            if (keyPos < 0 || keyPos >= keyData.Length)
                throw new InvalidDataException("Invalid key position.");

            var keyType = (CatalogKeyType)keyData[keyPos++];
            object key;

            switch (keyType)
            {
                case CatalogKeyType.Utf8String:
                case CatalogKeyType.UnicodeString:
                {
                    if (keyPos + 4 > keyData.Length)
                        throw new InvalidDataException("Invalid key length.");

                    var len = BinaryPrimitives.ReadInt32LittleEndian(keyData.AsSpan(keyPos));
                    keyPos += 4;

                    if (keyPos + len > keyData.Length)
                        throw new InvalidDataException("Invalid string length.");

                    var encoding = keyType == CatalogKeyType.UnicodeString
                        ? Encoding.Unicode
                        : Encoding.UTF8;

                    key = encoding.GetString(keyData, keyPos, len);
                    break;
                }

                case CatalogKeyType.Byte:
                    if (keyPos >= keyData.Length)
                        throw new InvalidDataException("Invalid byte key.");
                    key = keyData[keyPos];
                    break;

                default:
                    throw new InvalidOperationException();
            }

            var entryCount = reader.ReadInt32();
            var entryPos = reader.ReadInt32();

            for (var j = 1; j < entryCount; j++)
                reader.ReadInt32();

            temp.Add((key, (ushort)entryPos));
        }

        var result = new Dictionary<object, object?>();

        foreach (var (key, valueIndex) in temp)
        {
            var entryStart = 4 + 28 * valueIndex;

            if (entryStart + 9 >= entryData.Length)
                throw new InvalidDataException("Invalid entry data.");

            var raw = (ushort)(
                entryData[entryStart + 8] |
                (entryData[entryStart + 9] << 8));

            result[key] = raw;
        }

        ResolveReferences(result);

        return result;
    }

    private static void ResolveReferences(Dictionary<object, object?> table)
    {
        var keys = table.Keys.ToList();

        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var value = table[key];

            if (value is not ushort index)
                continue;

            if (index == ushort.MaxValue || index >= table.Count)
                continue;

            var resolvedKey = table.ElementAt(index).Key;
            table[key] = resolvedKey;
        }
    }
}