using System;
using System.Collections.Generic;
using PhiInfo.Core.Type;

namespace PhiInfo.Core;

public partial class PhiInfo
{
    public List<SongInfo> ExtractSongInfo()
    {
        var result = new List<SongInfo>();

        var gameInfoField = FindMonoBehaviour(
            _level0Inst,
            "GameInformation"
        ) ?? throw new Exception("GameInformation MonoBehaviour not found");

        var songField = gameInfoField["song"];
        var comboArray = gameInfoField["songAllCombos"]["Array"];

        var comboDict = new Dictionary<string, List<int>>();
        for (var i = 0; i < comboArray.Children.Count; i++)
        {
            var combo = comboArray[i];
            var songId = combo["songsId"].AsString;
            var allComboList = new List<int>();
            var allComboField = combo["allComboNum"]["Array"];
            for (var j = 0; j < allComboField.Children.Count; j++)
                allComboList.Add(allComboField[j].AsInt);
            comboDict[songId] = allComboList;
        }

        for (var i = 0; i < songField.Children.Count; i++)
        {
            var songArray = songField[i]["Array"];
            for (var j = 0; j < songArray.Children.Count; j++)
            {
                var song = songArray[j];
                var songId = song["songsId"].AsString;

                var allComboNum = comboDict.TryGetValue(songId, out var value) ? value : new List<int>();
                var levelsArray = song["levels"]["Array"];
                var chartersArray = song["charter"]["Array"];
                var difficultiesArray = song["difficulty"]["Array"];

                var levelsDict = new Dictionary<string, SongLevel>();

                for (var k = 0; k < difficultiesArray.Children.Count; k++)
                {
                    var diff = difficultiesArray[k].AsDouble;
                    if (diff == 0) continue;

                    var levelName = levelsArray[k].AsString;
                    var charter = chartersArray[k].AsString;
                    var allCombo = k < allComboNum.Count ? allComboNum[k] : 0;

                    levelsDict[levelName] = new SongLevel(
                        charter,
                        allCombo,
                        Math.Round(diff, 1)
                    );
                }

                if (levelsDict.Count == 0) continue;

                result.Add(new SongInfo(
                    songId,
                    song["songsKey"].AsString,
                    song["songsName"].AsString,
                    song["composer"].AsString,
                    song["illustrator"].AsString,
                    Math.Round(song["previewTime"].AsDouble, 2),
                    Math.Round(song["previewEndTime"].AsDouble, 2),
                    levelsDict
                ));
            }
        }

        return result;
    }

    public List<Folder> ExtractCollection()
    {
        var result = new List<Folder>();

        var collectionField = FindMonoBehaviour(
            _level22Inst,
            "SaturnOSControl"
        ) ?? throw new Exception("SaturnOSControl MonoBehaviour not found");

        var folders = collectionField["folders"]["Array"];

        for (var i = 0; i < folders.Children.Count; i++)
        {
            var folder = folders[i];
            var filesArray = folder["files"]["Array"];
            var files = new List<FileItem>();

            for (var j = 0; j < filesArray.Children.Count; j++)
            {
                var file = filesArray[j];

                files.Add(new FileItem(
                    file["key"].AsString,
                    file["subIndex"].AsInt,
                    file["name"][Lang].AsString,
                    file["date"].AsString,
                    file["supervisor"][Lang].AsString,
                    file["category"].AsString,
                    file["content"][Lang].AsString,
                    file["properties"][Lang].AsString
                ));
            }

            result.Add(new Folder(
                folder["title"][Lang].AsString,
                folder["subTitle"][Lang].AsString,
                folder["cover"].AsString,
                files
            ));
        }

        return result;
    }

    public List<Avatar> ExtractAvatars()
    {
        var result = new List<Avatar>();

        var avatarField = FindMonoBehaviour(
            _level0Inst,
            "GetCollectionControl"
        ) ?? throw new Exception("GetCollectionControl MonoBehaviour not found");

        var avatarsArray = avatarField["avatars"]["Array"];

        for (var i = 0; i < avatarsArray.Children.Count; i++)
        {
            var avatar = avatarsArray[i];
            result.Add(new Avatar(
                avatar["name"].AsString,
                avatar["addressableKey"].AsString
            ));
        }

        return result;
    }

    public List<string> ExtractTips()
    {
        var result = new List<string>();

        var tipsField = FindMonoBehaviour(
            _level0Inst,
            "TipsProvider"
        ) ?? throw new Exception("TipsProvider MonoBehaviour not found");

        var tipsArray = tipsField["tips"]["Array"];

        for (var i = 0; i < tipsArray.Children.Count; i++)
        {
            var tipsLang = tipsArray[i];
            if (tipsLang["language"].AsInt == LangId)
            {
                for (var j = 0; j < tipsLang["tips"]["Array"].Children.Count; j++)
                    result.Add(tipsLang["tips"]["Array"][j].AsString);

                break;
            }
        }

        return result;
    }

    public List<ChapterInfo> ExtractChapters()
    {
        var result = new List<ChapterInfo>();

        var chapterField = FindMonoBehaviour(
            _level0Inst,
            "GameInformation"
        ) ?? throw new Exception("GameInformation MonoBehaviour not found");

        var chaptersArray = chapterField["chapters"]["Array"];

        for (var i = 0; i < chaptersArray.Children.Count; i++)
        {
            var chapter = chaptersArray[i];
            var code = chapter["chapterCode"].AsString;
            var songInfo = chapter["songInfo"];
            var banner = songInfo["banner"].AsString;
            var songsArray = songInfo["songs"]["Array"];
            var songs = new List<string>();
            for (var j = 0; j < songsArray.Children.Count; j++) songs.Add(songsArray[j]["songsId"].AsString);

            result.Add(new ChapterInfo(code, banner, songs));
        }

        return result;
    }

    public AllInfo ExtractAll()
    {
        return new AllInfo(
            ExtractSongInfo(),
            ExtractCollection(),
            ExtractAvatars(),
            ExtractTips(),
            ExtractChapters()
        );
    }
}