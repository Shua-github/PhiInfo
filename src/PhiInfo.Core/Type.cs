#pragma warning disable IDE1006
#pragma warning disable IDE0130

using System;
using System.Collections.Generic;

namespace PhiInfo.Core.Type;

public record SongLevel(string charter, int all_combo_num, double difficulty);

public record SongInfo(
    string id,
    string key,
    string name,
    string composer,
    string illustrator,
    double preview_time,
    double preview_end_time,
    Dictionary<string, SongLevel> levels
);

public record Folder(
    string title,
    string sub_title,
    string cover,
    List<FileItem> files
);

public record FileItem(
    string key,
    int sub_index,
    string name,
    string date,
    string supervisor,
    string category,
    string content,
    string properties
);

public record Avatar(string name, string addressable_key);

public record AllInfo(
    List<SongInfo> songs,
    List<Folder> collection,
    List<Avatar> avatars,
    List<string> tips,
    List<ChapterInfo> chapters
);

public record Catalog(
    string m_KeyDataString,
    string m_BucketDataString,
    string m_EntryDataString
);

public record Image(uint format, uint width, uint height, byte[] data)
{
    public byte[] WithHeader()
    {
        var result = new byte[1 + 4 + 4 + 4 + data.Length];
        result[0] = 72; // 'H'
        BitConverter.GetBytes(format).CopyTo(result, 1);
        BitConverter.GetBytes(height).CopyTo(result, 5);
        BitConverter.GetBytes(width).CopyTo(result, 9);
        data.CopyTo(result, 13);
        return result;
    }
}

public record Music(float length, byte[] data);

public record Text(string content);

public record ChapterInfo(
    string code,
    string banner,
    List<string> songs
);