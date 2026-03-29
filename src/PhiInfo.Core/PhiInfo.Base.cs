using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Cpp2IL;
using AssetsTools.NET.Extra;
using LibCpp2IL;

namespace PhiInfo.Core
{
    public partial class PhiInfo : IDisposable
    {
        private bool _disposed;
        private readonly AssetsFile _ggmInst = new();
        private readonly AssetsFile _level0Inst = new();
        private readonly AssetsFile _level22Inst = new();
        private readonly ClassDatabaseFile _classDatabase;

        private readonly Cpp2IlTempGenerator _tempGen;

        private readonly List<AssetsFileReader> _readers = new();

        static readonly string Lang = "chinese";
        static readonly int LangId = 40;

        public PhiInfo(
            Stream globalGameManagers,
            Stream level0,
            Stream level22,
            byte[] il2CppSo,
            byte[] globalMetadata,
            Stream cldb)
        {
            var ggmReader = new AssetsFileReader(globalGameManagers);
            _readers.Add(ggmReader);
            _ggmInst.Read(ggmReader);

            var level0Reader = new AssetsFileReader(level0);
            _readers.Add(level0Reader);
            _level0Inst.Read(level0Reader);

            var level22Reader = new AssetsFileReader(level22);
            _readers.Add(level22Reader);
            _level22Inst.Read(level22Reader);

            var classPackage = new ClassPackageFile();
            using (var cldbReader = new AssetsFileReader(cldb))
            {
                classPackage.Read(cldbReader);
            }

            _classDatabase = classPackage.GetClassDatabase(_ggmInst.Metadata.UnityVersion);

            _tempGen = new Cpp2IlTempGenerator(globalMetadata, il2CppSo);
            _tempGen.SetUnityVersion(new UnityVersion(_ggmInst.Metadata.UnityVersion));
            _tempGen.InitializeCpp2IL();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var r in _readers)
            {
                r.Dispose();
            }

            _readers.Clear();

            _level0Inst.Close();
            _level22Inst.Close();
            _ggmInst.Close();

            _tempGen.Dispose();
        }

        private AssetTypeValueField GetBaseField(
            AssetsFile file,
            AssetFileInfo info,
            bool monoFields)
        {
            lock (file.Reader)
            {
                var offset = info.GetAbsoluteByteOffset(file);

                var template = GetTemplateBaseField(file, info, file.Reader, offset, monoFields);

                if (template == null)
                    throw new Exception($"Failed to build template for type {info.TypeId}");

                RefTypeManager refMan = new();
                refMan.FromTypeTree(file.Metadata);

                return template.MakeValue(file.Reader, offset, refMan);
            }
        }

        internal static AssetTypeValueField GetBaseField(AssetsFile file, AssetFileInfo info)
        {
            lock (file.Reader)
            {
                var offset = info.GetAbsoluteByteOffset(file);

                if (!file.Metadata.TypeTreeEnabled)
                    throw new Exception($"Failed to build template for type {info.TypeId}");
                var tt = file.Metadata.FindTypeTreeTypeByID(info.TypeId, info.GetScriptIndex(file));
                if (tt == null || tt.Nodes.Count <= 0)
                    throw new Exception($"Failed to build template for type {info.TypeId}");
                var template = new AssetTypeTemplateField();
                template.FromTypeTree(tt);

                RefTypeManager refMan = new();
                refMan.FromTypeTree(file.Metadata);

                return template.MakeValue(file.Reader, offset, refMan);
            }
        }

        private AssetTypeTemplateField? GetTemplateBaseField(
            AssetsFile file,
            AssetFileInfo info,
            AssetsFileReader? reader,
            long absByteStart,
            bool monoFields = false)
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
                var cldbType = _classDatabase.FindAssetClassByID(info.TypeId);
                if (cldbType == null)
                    return null;

                baseField = new AssetTypeTemplateField();
                baseField.FromClassDatabase(_classDatabase, cldbType);
            }

            // 3. MonoBehaviour: 使用 MonoTempGenerator 补充字段
            if (info.TypeId == (int)AssetClassID.MonoBehaviour && monoFields && reader != null)
            {
                // 保存原始位置
                var originalPosition = reader.Position;
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
                        monoScriptFile = _ggmInst;
                    }
                    else
                    {
                        throw new Exception("Unsupported MonoScript FileID");
                    }

                    var monoInfo = monoScriptFile.GetAssetInfo(scriptPtr.PathId);
                    if (monoInfo != null)
                    {
                        if (GetMonoScriptInfo(monoScriptFile, monoInfo,
                                out var assemblyName, out string? nameSpace, out string? className))
                        {
                            if (assemblyName == null || className == null || nameSpace == null)
                                throw new Exception("MonoScript info incomplete");

                            // 移除 .dll 扩展名
                            if (assemblyName.EndsWith(".dll"))
                            {
                                assemblyName = assemblyName.Substring(0, assemblyName.Length - 4);
                            }

                            var newBase = _tempGen.GetTemplateField(
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

        public uint GetPhiVersion()
        {
            var meta = LibCpp2IlMain.TheMetadata
                       ?? throw new Exception("il2cpp 未初始化");

            var assembly = meta.AssemblyDefinitions
                               .FirstOrDefault(a => a.AssemblyName.Name == "Assembly-CSharp")
                           ?? throw new Exception("找不到 Assembly-CSharp");

            var type = assembly.Image.Types?
                           .FirstOrDefault(t => t.FullName == "Constants")
                       ?? throw new Exception("找不到 Constants 类");

            var field = type.Fields?
                            .FirstOrDefault(f => f.Name == "IntVersion")
                        ?? throw new Exception("找不到 IntVersion 字段");

            var defaultValue = meta.GetFieldDefaultValue(field)?.Value
                               ?? throw new Exception("字段没有默认值");

            if (defaultValue is int intValue)
                return (uint)intValue;

            throw new Exception($"版本号类型异常: {defaultValue.GetType()}");
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
                monoFields: false);

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