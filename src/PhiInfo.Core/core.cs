namespace PhiInfo.Core;

using System;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Cpp2IL;
using AssetsTools.NET.Extra;

public class PhiInfo
{
    private readonly AssetsFile ggmInst = new();
    private readonly AssetsFile level0Inst = new();
    private readonly AssetsFile level22Inst = new();
    private readonly ClassDatabaseFile classDatabase = new();

    private readonly IMonoBehaviourTemplateGenerator tempGen;

    static readonly string lang = "chinese";
    static readonly int langId = 40;

    public PhiInfo(
        Stream globalGameManagers,
        Stream level0,
        Stream level22,
        byte[] il2cppSo,
        byte[] globalMetadata,
        Stream cldb)
    {
        ggmInst.Read(new AssetsFileReader(globalGameManagers));

        var ClassPackage = new ClassPackageFile();
        ClassPackage.Read(new AssetsFileReader(cldb));
        classDatabase = ClassPackage.GetClassDatabase(ggmInst.Metadata.UnityVersion);
        level0Inst.Read(new AssetsFileReader(level0));
        level22Inst.Read(new AssetsFileReader(level22));

        string tempIl2cpp = Path.GetTempFileName();
        string tempMetadata = Path.GetTempFileName();
        File.WriteAllBytes(tempIl2cpp, il2cppSo);
        File.WriteAllBytes(tempMetadata, globalMetadata);
        tempGen = new Cpp2IlTempGenerator(tempMetadata, tempIl2cpp);
    }
        private AssetTypeValueField GetBaseField(
            AssetsFile file,
            AssetFileInfo info,
            bool MonoFields = false)
        {
            long offset = info.GetAbsoluteByteOffset(file);
            
            var template = GetTemplateBaseField(file, info, file.Reader, offset, MonoFields);
            
            if (template == null)
                throw new Exception($"Failed to build template for type {info.TypeId}");

            RefTypeManager refMan = new RefTypeManager();
            refMan.FromTypeTree(file.Metadata);

            return template.MakeValue(file.Reader, offset, refMan);
        }

        private AssetTypeTemplateField? GetTemplateBaseField(
            AssetsFile file,
            AssetFileInfo info,
            AssetsFileReader reader,
            long absByteStart,
            bool MonoFields = false)
        {
            ushort scriptIndex = info.GetScriptIndex(file);

            AssetTypeTemplateField? baseField = null;

            // 1. 优先 TypeTree
            if (file.Metadata.TypeTreeEnabled)
            {
                var tt = file.Metadata.FindTypeTreeTypeByID(info.TypeId, scriptIndex);
                if (tt != null && tt.Nodes.Count > 0)
                {
                    baseField = new AssetTypeTemplateField();
                    baseField.FromTypeTree(tt);
                }
            }

            // 2. 回退到 ClassDatabase
            if (baseField == null)
            {
                var cldbType = classDatabase.FindAssetClassByID(info.TypeId);
                if (cldbType == null)
                    return null;

                baseField = new AssetTypeTemplateField();
                baseField.FromClassDatabase(classDatabase, cldbType);
            }

            // 3. MonoBehaviour: 使用 MonoTempGenerator 补充字段
            if (info.TypeId == (int)AssetClassID.MonoBehaviour && tempGen != null && MonoFields && reader != null)
            {
                // 保存原始位置
                long originalPosition = reader.Position;
                reader.Position = absByteStart;
                
                // 创建临时的 RefTypeManager 用于读取值
                RefTypeManager tempRefMan = new RefTypeManager();
                tempRefMan.FromTypeTree(file.Metadata);
                
                var mbBase = baseField.MakeValue(reader, absByteStart, tempRefMan);
                var scriptPtr = AssetPPtr.FromField(mbBase["m_Script"]);
                
                if (!scriptPtr.IsNull())
                {
                    // 确定 MonoScript 所在的文件
                    AssetsFile monoScriptFile;
                    if (scriptPtr.FileId == 0)
                    {
                        monoScriptFile = file;
                    }
                    else if (scriptPtr.FileId == 1)
                    {
                        monoScriptFile = ggmInst;
                    } else {
                        throw new Exception("Unsupported MonoScript FileID");
                    }

                    var monoInfo = monoScriptFile.GetAssetInfo(scriptPtr.PathId);
                    if (monoInfo != null)
                    {
                        if (GetMonoScriptInfo(monoScriptFile, monoInfo, 
                            out string? assemblyName, out string? nameSpace, out string? className))
                        {
                            if (assemblyName == null || className == null || nameSpace == null)
                                throw new Exception("MonoScript info incomplete");
                    
                            // 移除 .dll 扩展名
                            if (assemblyName.EndsWith(".dll"))
                            {
                                assemblyName = assemblyName.Substring(0, assemblyName.Length - 4);
                            }

                            var newBase = tempGen.GetTemplateField(
                                baseField,
                                assemblyName,
                                nameSpace,
                                className,
                                new UnityVersion(file.Metadata.UnityVersion));

                            if (newBase != null)
                            {
                                baseField = newBase;
                            }
                        }
                    }
                }
                
                // 恢复原始位置
                reader.Position = originalPosition;
                
            }

            return baseField;
        }

        private bool GetMonoScriptInfo(
            AssetsFile file,
            AssetFileInfo info,
            out string? assemblyName,
            out string? nameSpace,
            out string? className)
        {
            assemblyName = null;
            nameSpace = null;
            className = null;

            var template = GetTemplateBaseField(
                file, 
                info, 
                file.Reader, 
                info.GetAbsoluteByteOffset(file), 
                MonoFields: false);
            
            if (template == null)
                return false;

            long offset = info.GetAbsoluteByteOffset(file);
            file.Reader.Position = offset;
            
            RefTypeManager refMan = new RefTypeManager();
            refMan.FromTypeTree(file.Metadata);
            
            var valueField = template.MakeValue(file.Reader, offset, refMan);
            
            assemblyName = valueField["m_AssemblyName"]?.AsString;
            nameSpace = valueField["m_Namespace"]?.AsString;
            className = valueField["m_ClassName"]?.AsString;

            return !string.IsNullOrEmpty(assemblyName) && !string.IsNullOrEmpty(className);
        }

        private AssetTypeValueField? FindMonoBehaviour(
            AssetsFile file,
            string name)
        {
            foreach (var info in file.AssetInfos)
            {
                if (info.TypeId != (int)AssetClassID.MonoBehaviour)
                    continue;

                var baseField = GetBaseField(file, info, false);
                
                var scriptField = baseField["m_Script"];
                if (scriptField == null)
                    continue;
                    
                var msId = scriptField["m_PathID"].AsLong;
                if (msId == 0)
                    continue;
                    
                var monoInfo = ggmInst.GetAssetInfo(msId);
                if (monoInfo == null)
                    continue;
                    
                var msBase = GetBaseField(ggmInst, monoInfo, false);
                var msName = msBase["m_Name"]?.AsString;

                if (msName == name)
                {
                    return GetBaseField(file, info, true);
                }
            }

            return null;
        }

    public List<SongInfo> GetSongInfo()
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

    public List<Folder> GetCollection()
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

    public List<Avatar> GetAvatars()
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

    public List<string> GetTips()
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
}