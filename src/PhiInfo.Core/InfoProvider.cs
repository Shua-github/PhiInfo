using System;
using System.Collections.Generic;
using System.Linq;
using AssetsTools.NET;
using PhiInfo.Core.Type;

namespace PhiInfo.Core;

public class InfoProvider : IDisposable
{
    private readonly FieldProvider _fieldProvider;
    private readonly AssetsFile _level0 = new();
    private readonly AssetsFile _level22 = new();
    private bool _disposed;

    public InfoProvider(IInfoDataProvider dataProvider, FieldProvider fieldProvider,
        Language language = Language.Chinese)
    {
        Language = language;
        _fieldProvider = fieldProvider;
        _level0.Read(new AssetsFileReader(dataProvider.GetLevel0()));
        _level22.Read(new AssetsFileReader(dataProvider.GetLevel22()));
    }

    public Language Language { get; set; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            _level0.Close();
            _level22.Close();
            _fieldProvider.Dispose();
        }
    }

    public uint GetPhiVersion()
    {
        return FieldProvider.GetPhiVersion();
    }

    public List<SongInfo> ExtractSongInfo()
    {
        var gameInfo = _fieldProvider.FindMonoBehaviour(_level0, "GameInformation")
                       ?? throw new InvalidOperationException("GameInformation MonoBehaviour not found");

        var songs = gameInfo["song"]
            .Children
            .SelectMany(songGroup => songGroup["Array"].Children)
            .Select(song =>
            {
                var levels = song["levels"]["Array"].Children;
                var charters = song["charter"]["Array"].Children;
                var diffs = song["difficulty"]["Array"].Children;

                var levelDict = diffs
                    .Select((diffNode, i) => new
                    {
                        Diff = diffNode.AsDouble,
                        Level = levels[i].AsString,
                        Charter = charters[i].AsString
                    })
                    .Where(x => x.Diff != 0)
                    .ToDictionary(
                        x => x.Level,
                        x => new SongLevel(x.Charter, Math.Round(x.Diff, 1))
                    );

                return levelDict.Count == 0
                    ? null
                    : new SongInfo(
                        song["songsId"].AsString,
                        song["songsKey"].AsString,
                        song["songsName"].AsString,
                        song["composer"].AsString,
                        song["illustrator"].AsString,
                        Math.Round(song["previewTime"].AsDouble, 2),
                        Math.Round(song["previewEndTime"].AsDouble, 2),
                        levelDict
                    );
            })
            .Where(song => song != null)
            .ToList();

        return songs!;
    }

    public List<Folder> ExtractCollection()
    {
        var control = _fieldProvider.FindMonoBehaviour(_level22, "SaturnOSControl")
                      ?? throw new InvalidOperationException("SaturnOSControl MonoBehaviour not found");

        return control["folders"]["Array"].Children
            .Select(folder =>
            {
                var files = folder["files"]["Array"].Children
                    .Select(file => new FileItem(
                        file["key"].AsString,
                        file["subIndex"].AsInt,
                        file["name"][Language.GetStringId()].AsString,
                        file["date"].AsString,
                        file["supervisor"][Language.GetStringId()].AsString,
                        file["category"].AsString,
                        file["content"][Language.GetStringId()].AsString.Replace("\\n", "\n"),
                        file["properties"][Language.GetStringId()].AsString
                    ))
                    .ToList();

                return new Folder(
                    folder["title"][Language.GetStringId()].AsString,
                    folder["subTitle"][Language.GetStringId()].AsString,
                    folder["cover"].AsString,
                    files
                );
            })
            .ToList();
    }

    public List<Avatar> ExtractAvatars()
    {
        var control = _fieldProvider.FindMonoBehaviour(_level0, "GetCollectionControl")
                      ?? throw new InvalidOperationException("GetCollectionControl MonoBehaviour not found");

        return control["avatars"]["Array"].Children
            .Select(a => new Avatar(
                a["name"].AsString,
                a["addressableKey"].AsString
            ))
            .ToList();
    }

    public List<string> ExtractTips()
    {
        var provider = _fieldProvider.FindMonoBehaviour(_level0, "TipsProvider")
                       ?? throw new InvalidOperationException("TipsProvider MonoBehaviour not found");

        return provider["tips"]["Array"].Children
                   .FirstOrDefault(t => t["language"].AsInt == (int)Language)?["tips"]["Array"].Children
                   .Select(t => t.AsString)
                   .ToList()
               ?? [];
    }

    public List<ChapterInfo> ExtractChapters()
    {
        var gameInfo = _fieldProvider.FindMonoBehaviour(_level0, "GameInformation")
                       ?? throw new InvalidOperationException("GameInformation MonoBehaviour not found");

        return gameInfo["chapters"]["Array"].Children
            .Select(chapter =>
            {
                var songInfo = chapter["songInfo"];

                var songs = songInfo["songs"]["Array"].Children
                    .Select(s => s["songsId"].AsString)
                    .ToList();

                return new ChapterInfo(
                    chapter["chapterCode"].AsString,
                    songInfo["banner"].AsString,
                    songs
                );
            })
            .ToList();
    }

    public AllInfo ExtractAllInfo()
    {
        return new AllInfo(GetPhiVersion(),ExtractSongInfo(),ExtractCollection(),ExtractAvatars(),ExtractTips(),ExtractChapters());
    }
}