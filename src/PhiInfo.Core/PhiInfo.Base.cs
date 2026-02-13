using System;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Cpp2IL;
using AssetsTools.NET.Extra;

namespace PhiInfo.Core
{
    public partial class PhiInfo
    {
        private readonly AssetsFile ggmInst = new();
        private readonly AssetsFile level0Inst = new();
        private readonly AssetsFile level22Inst = new();
        private readonly ClassDatabaseFile classDatabase = new();

        private readonly Cpp2IlTempGenerator tempGen;

        static readonly string tempDir = "./temp/";

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

            Directory.CreateDirectory(tempDir);

            File.WriteAllBytes(tempDir+"libil2cpp.so", il2cppSo);
            File.WriteAllBytes(tempDir+"global-metadata.dat", globalMetadata);
            tempGen = new Cpp2IlTempGenerator(tempDir+"global-metadata.dat", tempDir+"libil2cpp.so");
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

            RefTypeManager refMan = new();
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
                RefTypeManager tempRefMan = new();
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
                    }
                    else
                    {
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

            RefTypeManager refMan = new();
            refMan.FromTypeTree(file.Metadata);

            var valueField = template.MakeValue(file.Reader, offset, refMan);

            assemblyName = valueField["m_AssemblyName"]?.AsString;
            nameSpace = valueField["m_Namespace"]?.AsString;
            className = valueField["m_ClassName"]?.AsString;

            return !string.IsNullOrEmpty(assemblyName) && !string.IsNullOrEmpty(className);
        }
    }
}