#pragma warning disable IDE1006
#pragma warning disable IDE0130

using System;
using System.Collections.Generic;
using System.IO;

namespace PhiInfo.Core.Type;

public record SongLevel(string charter, double difficulty);

public record SongInfo(
    string id,
    string key,
    string name,
    string composer,
    string illustrator,
    double preview_time,
    double preview_end_time,
    // 这里的key对应难度等级
    Dictionary<string, SongLevel> levels
)
{
    public string IllLowResPath()
    {
        return $"Assets/Tracks/{id}/IllustrationLowRes.jpg";
    }

    public string IllPath()
    {
        return $"Assets/Tracks/{id}/Illustration.jpg";
    }

    public string IllBlurPath()
    {
        return $"Assets/Tracks/{id}/IllustrationBlur.jpg";
    }

    public string MusicPath()
    {
        return $"Assets/Tracks/{id}/music.wav";
    }

    public string GetChartPath(string difficulty)
    {
        if (!levels.ContainsKey(difficulty))
            throw new ArgumentException("This song does not have requested difficulty.", nameof(difficulty));

        return $"Assets/Tracks/{id}/Chart_{difficulty}.json";
    }
}

public record Folder(
    string title,
    // 空字符串时不需要渲染
    string sub_title,
    // 为addressable_key
    string cover,
    List<FileItem> files
);

public record FileItem(
    string key,
    // 一般情况下不需要使用
    int sub_index,
    string name,
    string date,
    string supervisor,
    string category,
    string content,
    // 额外信息,单个 "名称=值" 结构,与其他信息并列
    string properties
);

public record Avatar(string name, string addressable_key);

public record Catalog(
    string m_KeyDataString,
    string m_BucketDataString,
    string m_EntryDataString
);

public record Image(uint format, uint width, uint height, byte[] data);

public record Music(float length, byte[] data);

public record Text(string content);

public record ChapterInfo(
    string code,
    string banner,
    List<string> song_ids
);

public record AllInfo(
    uint version,
    List<SongInfo> songs,
    List<Folder> collection,
    List<Avatar> avatars,
    List<string> tips,
    List<ChapterInfo> chapters);

[AttributeUsage(AttributeTargets.Field)]
public class LanguageStringIdAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}

public enum Language
{
    [LanguageStringId("chinese")] Chinese = 0x28,

    [LanguageStringId("chineseTraditional")]
    TraditionalChinese = 0x29,

    [LanguageStringId("english")] English = 0x0A,

    [LanguageStringId("japanese")] Japanese = 0x16,

    [LanguageStringId("korean")] Korean = 0x17
}

public interface IDataProvider : IDisposable, IFieldDataProvider, IInfoDataProvider, ICatalogDataProvider,
    IAssetDataProvider;

public interface IFieldDataProvider
{
    Stream GetCldb();
    Stream GetGlobalGameManagers();
    byte[] GetIl2CppBinary();
    byte[] GetGlobalMetadata();
}

public interface IInfoDataProvider
{
    Stream GetLevel0();
    Stream GetLevel22();
}

public interface ICatalogDataProvider
{
    Stream GetCatalog();
}

public interface IAssetDataProvider
{
    Stream GetBundle(string name);
}