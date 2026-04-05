#pragma warning disable IDE1006
#pragma warning disable IDE0130

using System.Collections.Generic;
using PhiInfo.Core.Type;

namespace PhiInfo.Processing.Type;

public record AppInfo(string version, string type);

public record ServerInfo(string version, string platform, AppInfo app);

public record AllInfo(
    uint version,
    List<SongInfo> songs,
    List<Folder> collection,
    List<Avatar> avatars,
    List<string> tips,
    List<ChapterInfo> chapters);

public record Response(ushort code, string mime, byte[]? data);