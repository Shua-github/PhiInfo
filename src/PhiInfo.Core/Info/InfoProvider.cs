using System;
using System.Collections.Generic;
using System.Linq;
using AssetsTools.NET;
using PhiInfo.Core.Type;

namespace PhiInfo.Core.Info;

public class InfoProvider : IDisposable
{
    private readonly FieldProvider _fieldProvider;
    private readonly Lazy<AssetsFile> _level0;
    private readonly Lazy<AssetsFile> _level22;
    private bool _disposed;

    public InfoProvider(IInfoDataProvider dataProvider, FieldProvider fieldProvider)
    {
        _fieldProvider = fieldProvider;
        _level0 = new Lazy<AssetsFile>(() =>
        {
            var file = new AssetsFile();
            file.Read(new AssetsFileReader(dataProvider.GetLevel0()));
            return file;
        });

        _level22 = new Lazy<AssetsFile>(() =>
        {
            var file = new AssetsFile();
            file.Read(new AssetsFileReader(dataProvider.GetLevel22()));
            return file;
        });
    }

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
            if (_level0.IsValueCreated)
                _level0.Value.Close();

            if (_level22.IsValueCreated)
                _level22.Value.Close();
        }
    }

    private static Dictionary<Language, string> ExtractMultiLang(AssetTypeValueField field,
        Func<string, string>? hook = null)
    {
        var result = new Dictionary<Language, string>();

        foreach (var child in field.Children)
        {
            if (child.FieldName == "code")
                continue;

            var lang = Extensions.FromString(child.FieldName);
            var value = child.AsString;

            if (hook != null)
                value = hook(value);

            result[lang] = value;
        }

        return result;
    }

    public List<SongInfo> ExtractSongs()
    {
        var gameInfo = _fieldProvider.FindMonoBehaviour(_level0.Value, "GameInformation")
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
        var control = _fieldProvider.FindMonoBehaviour(_level22.Value, "SaturnOSControl")
                      ?? throw new InvalidOperationException("SaturnOSControl MonoBehaviour not found");

        return control["folders"]["Array"].Children
            .Select(folder =>
            {
                var files = folder["files"]["Array"].Children
                    .Select(file => new FileItem(
                        file["key"].AsString,
                        file["subIndex"].AsInt,
                        ExtractMultiLang(file["name"]),
                        file["date"].AsString,
                        ExtractMultiLang(file["supervisor"]),
                        file["category"].AsString,
                        ExtractMultiLang(file["content"], v => v.Replace("\\n", "\n")),
                        ExtractMultiLang(file["properties"])
                    ))
                    .ToList();

                return new Folder(
                    ExtractMultiLang(folder["title"]),
                    ExtractMultiLang(folder["subTitle"]),
                    folder["cover"].AsString,
                    files
                );
            })
            .ToList();
    }

    public List<Avatar> ExtractAvatars()
    {
        var control = _fieldProvider.FindMonoBehaviour(_level0.Value, "GetCollectionControl")
                      ?? throw new InvalidOperationException("GetCollectionControl MonoBehaviour not found");

        return control["avatars"]["Array"].Children
            .Select(a => new Avatar(
                a["name"].AsString,
                a["addressableKey"].AsString
            ))
            .ToList();
    }

    public Dictionary<Language, List<string>> ExtractTips()
    {
        var provider = _fieldProvider.FindMonoBehaviour(_level0.Value, "TipsProvider")
                       ?? throw new InvalidOperationException("TipsProvider MonoBehaviour not found");

        var result = new Dictionary<Language, List<string>>();

        var array = provider["tips"]["Array"].Children;

        foreach (var entry in array)
        {
            var langValue = entry["language"].AsInt;
            var language = Extensions.FromInt(langValue);

            var tips = entry["tips"]["Array"].Children
                .Select(t => t.AsString)
                .ToList();

            result[language] = tips;
        }

        return result;
    }

    public List<ChapterInfo> ExtractChapters()
    {
        var gameInfo = _fieldProvider.FindMonoBehaviour(_level0.Value, "GameInformation")
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
        return new AllInfo(_fieldProvider.GetPhiVersion(), ExtractSongs(), ExtractCollection(), ExtractAvatars(),
            ExtractTips(), ExtractChapters());
    }
}