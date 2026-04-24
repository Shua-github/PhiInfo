using System;
using System.Collections.Generic;
using PhiInfo.Core.Type;

namespace PhiInfo.Core.Info;

public record SongLevel(
    // 谱师
    string charter,
    // 定数
    double difficulty
);

public record SongInfo(
    string id,
    // keyStore用的
    string key,
    // 名称
    string name,
    // 曲师
    string composer,
    // 画师
    string illustrator,
    // 预览时间
    double preview_time,
    double preview_end_time,
    // key=难度等级
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
    Dictionary<Language, string> title,
    // 空字符串时不需要渲染
    Dictionary<Language, string> sub_title,
    // 为addressable_key
    string cover,
    List<FileItem> files
);

public record FileItem(
    // keyStore用的
    string key,
    // 云存档用的
    int sub_index,
    // 名称
    Dictionary<Language, string> name,
    // 收集时间
    string date,
    // 保管单位
    Dictionary<Language, string> supervisor,
    // 等级
    string category,
    // 内容
    Dictionary<Language, string> content,
    // 额外信息,单个 "名称=值" 结构,与其他信息并列
    Dictionary<Language, string> properties
);

public record Avatar(string name, string addressable_key);

public record ChapterInfo(
    string code,
    // 目前无法提取名称,可以用横幅当名称
    string banner,
    List<string> song_ids
)
{
    public string CoverBlurPath()
    {
        if (code == "MainStory8") return "Assets/Tracks/#ChapterCover/MainStory8_2BlurS.jpg";
        return $"Assets/Tracks/#ChapterCover/{code}Blur.jpg";
    }

    public string CoverPath()
    {
        if (code == "MainStory8") return "Assets/Tracks/#ChapterCover/MainStory8_2S.jpg";
        return $"Assets/Tracks/#ChapterCover/{code}.jpg";
    }
}

public record PhiVersion(uint code, string name);

public record AllInfo(
    PhiVersion version,
    List<SongInfo> songs,
    List<Folder> collection,
    List<Avatar> avatars,
    Dictionary<Language, List<string>> tips,
    List<ChapterInfo> chapters);