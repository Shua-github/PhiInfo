using System;
using System.Collections.Generic;
using PhiInfo.Core.Type;

namespace PhiInfo.Core
{
    public partial class PhiInfo
    {
        public List<SongInfo> ExtractSongInfo()
        {
            var result = new List<SongInfo>();

            var gameInfoField = FindMonoBehaviour(
                level0Inst,
                "GameInformation"
            ) ?? throw new Exception("GameInformation MonoBehaviour not found");

            var songField = gameInfoField["song"];
            var comboArray = gameInfoField["songAllCombos"]["Array"];

            var comboDict = new Dictionary<string, List<int>>();
            for (int i = 0; i < comboArray.Children.Count; i++)
            {
                var combo = comboArray[i];
                string songId = combo["songsId"].AsString;
                var allComboList = new List<int>();
                var allComboField = combo["allComboNum"]["Array"];
                for (int j = 0; j < allComboField.Children.Count; j++)
                    allComboList.Add(allComboField[j].AsInt);
                comboDict[songId] = allComboList;
            }

            for (int i = 0; i < songField.Children.Count; i++)
            {
                var songArray = songField[i]["Array"];
                for (int j = 0; j < songArray.Children.Count; j++)
                {
                    var song = songArray[j];
                    string songId = song["songsId"].AsString;

                    var allComboNum = comboDict.ContainsKey(songId) ? comboDict[songId] : new List<int>();
                    var levelsArray = song["levels"]["Array"];
                    var chartersArray = song["charter"]["Array"];
                    var difficultiesArray = song["difficulty"]["Array"];

                    var levelsDict = new Dictionary<string, SongLevel>();

                    for (int k = 0; k < difficultiesArray.Children.Count; k++)
                    {
                        double diff = difficultiesArray[k].AsDouble;
                        if (diff == 0) continue;

                        string levelName = levelsArray[k].AsString;
                        string charter = chartersArray[k].AsString;
                        int allCombo = k < allComboNum.Count ? allComboNum[k] : 0;

                        levelsDict[levelName] = new SongLevel
                        {
                            charter = charter,
                            all_combo_num = allCombo,
                            difficulty = Math.Round(diff, 1)
                        };
                    }

                    if (levelsDict.Count == 0) continue;

                    result.Add(new SongInfo
                    {
                        id = songId,
                        name = song["songsName"].AsString,
                        composer = song["composer"].AsString,
                        illustrator = song["illustrator"].AsString,
                        preview_time = Math.Round(song["previewTime"].AsDouble, 2),
                        preview_end_time = Math.Round(song["previewEndTime"].AsDouble, 2),
                        levels = levelsDict
                    });
                }
            }

            return result;
        }

        public List<Folder> ExtractCollection()
        {
            var result = new List<Folder>();

            var collectionField = FindMonoBehaviour(
                level22Inst,
                "SaturnOSControl"
            ) ?? throw new Exception("SaturnOSControl MonoBehaviour not found");

            var folders = collectionField["folders"]["Array"];

            for (int i = 0; i < folders.Children.Count; i++)
            {
                var folder = folders[i];
                var filesArray = folder["files"]["Array"];
                var files = new List<FileItem>();

                for (int j = 0; j < filesArray.Children.Count; j++)
                {
                    var file = filesArray[j];

                    files.Add(new FileItem
                    {
                        key = file["key"].AsString,
                        sub_index = file["subIndex"].AsInt,
                        name = file["name"][lang].AsString,
                        date = file["date"].AsString,
                        supervisor = file["supervisor"][lang].AsString,
                        category = file["category"].AsString,
                        content = file["content"][lang].AsString,
                        properties = file["properties"][lang].AsString
                    });
                }

                result.Add(new Folder
                {
                    title = folder["title"][lang].AsString,
                    sub_title = folder["subTitle"][lang].AsString,
                    cover = folder["cover"].AsString,
                    files = files
                });
            }

            return result;
        }

        public List<Avatar> ExtractAvatars()
        {
            var result = new List<Avatar>();

            var avatarField = FindMonoBehaviour(
                level0Inst,
                "GetCollectionControl"
            ) ?? throw new Exception("GetCollectionControl MonoBehaviour not found");

            var avatarsArray = avatarField["avatars"]["Array"];

            for (int i = 0; i < avatarsArray.Children.Count; i++)
            {
                var avatar = avatarsArray[i];
                result.Add(new Avatar
                {
                    name = avatar["name"].AsString,
                    addressable_key = avatar["addressableKey"].AsString
                });
            }

            return result;
        }

        public List<string> ExtractTips()
        {
            var result = new List<string>();

            var tipsField = FindMonoBehaviour(
                level0Inst,
                "TipsProvider"
            ) ?? throw new Exception("TipsProvider MonoBehaviour not found");

            var tipsArray = tipsField["tips"]["Array"];

            for (int i = 0; i < tipsArray.Children.Count; i++)
            {
                var tipslang = tipsArray[i];
                if (tipslang["language"].AsInt == langId)
                {
                    for (int j = 0; j < tipslang["tips"]["Array"].Children.Count; j++)
                    {
                        result.Add(tipslang["tips"]["Array"][j].AsString);
                    }
                    break;
                }
            }

            return result;
        }

        public AllInfo ExtractAll()
        {
            return new AllInfo
            {
                songs = ExtractSongInfo(),
                collection = ExtractCollection(),
                avatars = ExtractAvatars(),
                tips = ExtractTips()
            };
        }
    }
}