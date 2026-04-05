using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AssetsTools.NET;
using PhiInfo.Core.Type;

namespace PhiInfo.Core;

public static class Extensions
{
    private static readonly Dictionary<Language, LanguageStringIdAttribute> LangAttributeMap = typeof(Language)
        .GetFields(BindingFlags.Static | BindingFlags.Public)
        .ToDictionary(x => (Language)x.GetValue(null)!,
            x => x.GetCustomAttribute<LanguageStringIdAttribute>() ?? throw new ArgumentNullException());

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

    public static string GetStringId(this Language lang)
    {
        return LangAttributeMap[lang].Id;
    }
}