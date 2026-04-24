using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhiInfo.Core.Asset;

internal record RawCatalog(
    [property: JsonPropertyName("m_KeyDataString")]
    string KeyDataString,
    [property: JsonPropertyName("m_BucketDataString")]
    string BucketDataString,
    [property: JsonPropertyName("m_EntryDataString")]
    string EntryDataString,
    [property: JsonPropertyName("m_InternalIds")]
    string[] InternalIds,
    [property: JsonPropertyName("m_InternalIdPrefixes")]
    string[] InternalIdPrefixes,
    [property: JsonPropertyName("m_ProviderIds")]
    string[] ProviderIds
);

[JsonSerializable(typeof(RawCatalog))]
internal partial class JsonContext : JsonSerializerContext
{
}

internal static class CatalogParser
{
    private const string BundledAssetProvider =
        "UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider";

    private const string AssetBundleProvider =
        "UnityEngine.ResourceManagement.ResourceProviders.AssetBundleProvider";

    public static Dictionary<string, string> Parse(Stream json)
    {
        RawCatalog catalog;
        using (json)
        {
            catalog = JsonSerializer.Deserialize(json, JsonContext.Default.RawCatalog) ??
                      throw new InvalidOperationException("Failed to deserialize catalog.");
        }

        var buckets = ParseBuckets(catalog.BucketDataString);
        var keys = ParseKeys(catalog.KeyDataString, buckets);
        var entries = ParseEntries(
            catalog.EntryDataString,
            catalog.ProviderIds,
            catalog.InternalIds,
            catalog.InternalIdPrefixes);

        var result = new Dictionary<string, string>(keys.Count, StringComparer.Ordinal);
        for (var i = 0; i < keys.Count; i++)
        {
            var key = keys[i];
            var bundle = FindBundleForKey(buckets[i], entries, buckets);
            if (!string.IsNullOrWhiteSpace(bundle))
                result[key] = bundle;
        }

        return result;
    }

    private static string? FindBundleForKey(Bucket bucket, IReadOnlyList<Entry> entries, IReadOnlyList<Bucket> buckets)
    {
        foreach (var entryIndex in bucket.Entries)
        {
            if (!IsValidIndex(entryIndex, entries.Count)) continue;

            var entry = entries[entryIndex];
            if (!string.Equals(entry.ProviderId, BundledAssetProvider, StringComparison.Ordinal)) continue;

            var bundle = FindBundleFromDependency(entry, entries, buckets);
            if (!string.IsNullOrWhiteSpace(bundle)) return bundle;
        }

        return null;
    }

    private static string? FindBundleFromDependency(Entry entry, IReadOnlyList<Entry> entries,
        IReadOnlyList<Bucket> buckets)
    {
        if (!IsValidIndex(entry.DependencyKeyIndex, buckets.Count)) return null;

        var dependencyBucket = buckets[entry.DependencyKeyIndex];
        string? fallbackBundle = null;

        foreach (var depEntryIndex in dependencyBucket.Entries)
        {
            if (!IsValidIndex(depEntryIndex, entries.Count)) continue;

            var depEntry = entries[depEntryIndex];
            if (string.Equals(depEntry.ProviderId, AssetBundleProvider, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(depEntry.InternalId)) return depEntry.InternalId;

            fallbackBundle ??= string.IsNullOrWhiteSpace(depEntry.InternalId) ? null : depEntry.InternalId;
        }

        return fallbackBundle;
    }

    private static List<Bucket> ParseBuckets(string bucketDataString)
    {
        ReadOnlySpan<byte> span = Convert.FromBase64String(bucketDataString);
        var offset = 0;

        var bucketCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset));
        offset += 4;
        var buckets = new List<Bucket>(bucketCount);

        for (var i = 0; i < bucketCount; i++)
        {
            var bucketOffset = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset));
            offset += 4;

            var entryCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset));
            offset += 4;

            var entries = new int[entryCount];
            for (var j = 0; j < entryCount; j++)
            {
                entries[j] = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset));
                offset += 4;
            }

            buckets.Add(new Bucket(bucketOffset, entries));
        }

        return buckets;
    }

    private static List<string> ParseKeys(string keyDataString, IReadOnlyList<Bucket> buckets)
    {
        var bytes = Convert.FromBase64String(keyDataString);
        ReadOnlySpan<byte> span = bytes;
        var offset = 0;

        var keyCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset));
        offset += 4;
        var keys = new List<string>(keyCount);

        var decodeCount = Math.Min(keyCount, buckets.Count);
        for (var i = 0; i < decodeCount; i++)
        {
            var startOffset = buckets[i].Offset;
            var key = DecodeKeyString(span, startOffset);
            keys.Add(key);
        }

        return keys;
    }

    private static List<Entry> ParseEntries(
        string entryDataString,
        string[] providerIds,
        string[] internalIds,
        string[] internalIdPrefixes)
    {
        var bytes = Convert.FromBase64String(entryDataString);
        ReadOnlySpan<byte> span = bytes;
        var offset = 0;

        var entryCount = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset));
        offset += 4;
        var entries = new List<Entry>(entryCount);

        for (var i = 0; i < entryCount; i++)
        {
            var internalIdIndex = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset));
            offset += 4;

            var providerIndex = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset));
            offset += 4;

            var dependencyKeyIndex = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset));
            offset += 4;

            // depHash 
            offset += 4;
            // dataIndex
            offset += 4;
            // primaryKeyIndex
            offset += 4;
            // resourceTypeIndex
            offset += 4;

            var providerId = IsValidIndex(providerIndex, providerIds.Length)
                ? providerIds[providerIndex]
                : string.Empty;

            var internalId = IsValidIndex(internalIdIndex, internalIds.Length)
                ? ExpandInternalId(internalIds[internalIdIndex], internalIdPrefixes)
                : string.Empty;

            entries.Add(new Entry(internalId, providerId, dependencyKeyIndex));
        }

        return entries;
    }

    private static string ExpandInternalId(string value, string[] internalIdPrefixes)
    {
        if (string.IsNullOrEmpty(value) || internalIdPrefixes.Length == 0) return value;

        var splitIndex = value.IndexOf('#');
        if (splitIndex <= 0 || splitIndex == value.Length - 1) return value;

        var prefixPart = value.AsSpan(0, splitIndex);
#if NET10_0_OR_GREATER
        if (!int.TryParse(prefixPart, out var prefixIndex) ||
            !IsValidIndex(prefixIndex, internalIdPrefixes.Length))
            return value;

        return string.Concat(internalIdPrefixes[prefixIndex].AsSpan(), value.AsSpan(splitIndex + 1));
#else
        if (!int.TryParse(prefixPart.ToString(), out var prefixIndex) ||
            !IsValidIndex(prefixIndex, internalIdPrefixes.Length))
            return value;

        return internalIdPrefixes[prefixIndex] + value.Substring(splitIndex + 1);
#endif
    }

    private static string DecodeKeyString(ReadOnlySpan<byte> data, int startOffset)
    {
        var offset = startOffset;
        var type = (KeyType)data[offset];
        offset++;

        switch (type)
        {
            case KeyType.AsciiString:
                return ReadAsciiString(data, offset);
            case KeyType.UnicodeString:
                return ReadUnicodeString(data, offset);
            case KeyType.UInt16:
                var ushortValue = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset));
                return ushortValue.ToString();
            case KeyType.UInt32:
                var uintValue = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset));
                return uintValue.ToString();
            case KeyType.Int32:
                var intValue = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset));
                return intValue.ToString();
            default:
                throw new InvalidDataException($"Unsupported key type: {type}");
        }
    }

    private static string ReadAsciiString(ReadOnlySpan<byte> data, int startOffset)
    {
        var offset = startOffset;
        var length = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset));
        offset += 4;

        if (length <= 0) return string.Empty;

        var stringBytes = data.Slice(offset, length);
#if NET10_0_OR_GREATER
        return Encoding.ASCII.GetString(stringBytes);
#else
        return Encoding.ASCII.GetString(stringBytes.ToArray());
#endif
    }

    private static string ReadUnicodeString(ReadOnlySpan<byte> data, int startOffset)
    {
        var offset = startOffset;
        var byteLength = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset));
        offset += 4;

        if (byteLength <= 0) return string.Empty;

        var stringBytes = data.Slice(offset, byteLength);
#if NET10_0_OR_GREATER
        return Encoding.Unicode.GetString(stringBytes);
#else
        return Encoding.Unicode.GetString(stringBytes.ToArray());
#endif
    }

    private static bool IsValidIndex(int index, int count)
    {
        return index >= 0 && index < count;
    }

    private enum KeyType
    {
        AsciiString,
        UnicodeString,
        UInt16,
        UInt32,
        Int32,
        Hash128,
        Type,
        JsonObject
    }

    private readonly record struct Bucket(int Offset, int[] Entries);

    private readonly record struct Entry(string InternalId, string ProviderId, int DependencyKeyIndex);
}