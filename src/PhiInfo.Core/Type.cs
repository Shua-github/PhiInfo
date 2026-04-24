#pragma warning disable IDE1006
#pragma warning disable IDE0130

using System;
using System.Text.Json.Serialization;
using PhiInfo.Core.Asset;
using PhiInfo.Core.Info;

namespace PhiInfo.Core.Type;

[AttributeUsage(AttributeTargets.Field)]
public class LanguageStringIdAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}

[JsonConverter(typeof(JsonStringEnumConverter<Language>))]
public enum Language
{
    [LanguageStringId("chinese")] zh_cn = 0x28,

    [LanguageStringId("chineseTraditional")]
    zh_tw = 0x29,

    [LanguageStringId("english")] en = 0x0A,

    [LanguageStringId("japanese")] ja = 0x16,

    [LanguageStringId("korean")] ko = 0x17
}

public interface IDataProvider : IDisposable, IFieldDataProvider, IInfoDataProvider, IAssetDataProvider;