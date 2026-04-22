using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AssetsTools.NET;
using PhiInfo.Core.Type;

#if !NET7_0_OR_GREATER
using System.IO;

#else
#endif

namespace PhiInfo.Core;

public static class Extensions
{
    internal static AssetTypeValueField GetBaseField(this AssetsFile file, AssetFileInfo info)
    {
        lock (file.Reader)
        {
            var offset = info.GetAbsoluteByteOffset(file);

            if (!file.Metadata.TypeTreeEnabled)
                throw new Exception($"Failed to build template for type {info.TypeId}");
            var tt = file.Metadata.FindTypeTreeTypeByID(info.TypeId, info.GetScriptIndex(file));
            if (tt == null || tt.Nodes.Count <= 0)
                throw new Exception($"Failed to build template for type {info.TypeId}");
            AssetTypeTemplateField template = new();
            template.FromTypeTree(tt);

            RefTypeManager refMan = new();
            refMan.FromTypeTree(file.Metadata);

            return template.MakeValue(file.Reader, offset, refMan);
        }
    }

    private static readonly Dictionary<string, Language> LangFromStringMap =
        typeof(Language)
            .GetFields(BindingFlags.Static | BindingFlags.Public)
            .ToDictionary(
                x => x.GetCustomAttribute<LanguageStringIdAttribute>()?.Id
                     ?? throw new ArgumentNullException(),
                x => (Language)x.GetValue(null)!
            );

    private static readonly Dictionary<int, Language> LangFromIntMap =
        typeof(Language)
            .GetFields(BindingFlags.Static | BindingFlags.Public)
            .ToDictionary(
                x => Convert.ToInt32((Language)x.GetValue(null)!),
                x => (Language)x.GetValue(null)!
            );

    internal static Language FromString(string id)
    {
        if (LangFromStringMap.TryGetValue(id, out var lang))
            return lang;

        throw new ArgumentException($"Unknown language string id: {id}");
    }

    internal static Language FromInt(int value)
    {
        if (LangFromIntMap.TryGetValue(value, out var lang))
            return lang;

        throw new ArgumentException($"Unknown language value: {value}");
    }
#if !NET7_0_OR_GREATER
    public static void ReadExactly(this Stream stream, byte[] buffer, int offset, int count)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = stream.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
                throw new EndOfStreamException();

            totalRead += read;
        }
    }
#else
#endif
}