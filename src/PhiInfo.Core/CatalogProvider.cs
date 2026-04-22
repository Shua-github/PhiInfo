using System;
using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhiInfo.Core.Type;

namespace PhiInfo.Core;

public record struct Key
{
    public byte Byte;
    public CatalogKeyType Kind;
    public string? Str;

    public bool IsString => Kind == CatalogKeyType.Utf8String || Kind == CatalogKeyType.UnicodeString;
}

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
    // 现在存储 Key 结构体
    private readonly ImmutableArray<KeyValuePair<Key, Key?>> _entries;
    private readonly FrozenDictionary<string, Key?> _stringIndex;

    public CatalogProvider(ICatalogDataProvider dataProvider)
    {
        using var catalog = dataProvider.GetCatalog();
        var json = JsonSerializer.Deserialize(catalog, JsonContext.Default.Catalog)
                   ?? throw new InvalidOperationException("Failed to deserialize catalog.");

        ReadOnlySpan<byte> keyData = Convert.FromBase64String(json.m_KeyDataString);
        ReadOnlySpan<byte> bucketData = Convert.FromBase64String(json.m_BucketDataString);
        ReadOnlySpan<byte> entryData = Convert.FromBase64String(json.m_EntryDataString);

        var rawEntries = ParseToEntryList(keyData, bucketData, entryData);
        var resultList = ResolveReferences(rawEntries);

        _entries = resultList.ToImmutableArray();

        var dictBuilder = new Dictionary<string, Key?>(resultList.Count);
        foreach (var kvp in resultList)
            if (kvp.Key.IsString && kvp.Key.Str != null)
                dictBuilder[kvp.Key.Str] = kvp.Value;

        _stringIndex = dictBuilder.ToFrozenDictionary();
    }

    private static List<RawEntry> ParseToEntryList(
        ReadOnlySpan<byte> keyData,
        ReadOnlySpan<byte> bucketData,
        ReadOnlySpan<byte> entryData)
    {
        if (bucketData.Length < 4) throw new InvalidDataException("Bucket data too short.");

        var bucketCount = BinaryPrimitives.ReadInt32LittleEndian(bucketData);
        var result = new List<RawEntry>(bucketCount);
        var bucketOffset = 4;

        for (var i = 0; i < bucketCount; i++)
        {
            var keyPos = BinaryPrimitives.ReadInt32LittleEndian(bucketData.Slice(bucketOffset, 4));
            bucketOffset += 4;

            if (keyPos < 0 || keyPos >= keyData.Length) throw new InvalidDataException("Invalid key position.");

            var type = (CatalogKeyType)keyData[keyPos++];
            var key = new Key { Kind = type };

            switch (type)
            {
                case CatalogKeyType.Utf8String:
                case CatalogKeyType.UnicodeString:
                    key.Str = ParseStringValue(type, keyData, ref keyPos);
                    break;
                case CatalogKeyType.Byte:
                    key.Byte = keyData[keyPos++];
                    break;
                default:
                    throw new NotSupportedException($"Unknown catalog key type: {type}");
            }

            var entryCount = BinaryPrimitives.ReadInt32LittleEndian(bucketData.Slice(bucketOffset, 4));
            bucketOffset += 4;
            var entryPos = BinaryPrimitives.ReadInt32LittleEndian(bucketData.Slice(bucketOffset, 4));
            bucketOffset += 4;

            bucketOffset += (entryCount - 1) * 4;

            var entryStart = 4 + 28 * entryPos;
            if (entryStart + 8 + 2 > entryData.Length) throw new InvalidDataException("Entry data bounds exceeded.");

            var rawIndex = BinaryPrimitives.ReadUInt16LittleEndian(entryData.Slice(entryStart + 8, 2));

            result.Add(new RawEntry(key, rawIndex));
        }

        return result;
    }

    private static string ParseStringValue(CatalogKeyType type, ReadOnlySpan<byte> data, ref int pos)
    {
        var len = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4));
        pos += 4;


#if NETSTANDARD2_1 || NET5_0_OR_GREATER
        var array = data.Slice(pos, len);
#else
        var array = data.Slice(pos, len).ToArray();
#endif

        var val = type == CatalogKeyType.UnicodeString
            ? Encoding.Unicode.GetString(array)
            : Encoding.UTF8.GetString(array);

        pos += len;
        return val;
    }

    private static List<KeyValuePair<Key, Key?>> ResolveReferences(List<RawEntry> rawEntries)
    {
        var result = new List<KeyValuePair<Key, Key?>>(rawEntries.Count);

        for (var i = 0; i < rawEntries.Count; i++)
        {
            var entry = rawEntries[i];
            Key? resolvedValue = null;

            if (entry.RawIndex != ushort.MaxValue && entry.RawIndex < rawEntries.Count)
                resolvedValue = rawEntries[entry.RawIndex].Key;

            result.Add(new KeyValuePair<Key, Key?>(entry.Key, resolvedValue));
        }

        return result;
    }

    public ImmutableArray<KeyValuePair<Key, Key?>> GetAll()
    {
        return _entries;
    }

    public Key? Get(string key)
    {
        return _stringIndex.TryGetValue(key, out var value) ? value : null;
    }

    public string[] GetStringKeys()
    {
        return _stringIndex.Keys.ToArray();
    }

    private readonly struct RawEntry
    {
        public readonly Key Key; // 使用结构体
        public readonly ushort RawIndex;

        public RawEntry(Key key, ushort rawIndex)
        {
            Key = key;
            RawIndex = rawIndex;
        }
    }
}