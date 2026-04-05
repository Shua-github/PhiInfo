using System;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Cpp2IL;
using AssetsTools.NET.Extra;
using LibCpp2IL;
using PhiInfo.Core.Type;

namespace PhiInfo.Core;

public class FieldProvider : IDisposable
{
    private readonly ClassDatabaseFile _classDatabase;

    private readonly AssetsFile _globalGameManagers = new();

    private readonly Cpp2IlTempGenerator _templateGenerator;
    private bool _disposed;

    public FieldProvider(IFieldDataProvider dataProvider)
    {
        _globalGameManagers.Read(new AssetsFileReader(dataProvider.GetGlobalGameManagers()));

        ClassPackageFile classPackage = new();
        using AssetsFileReader cldbReader = new(dataProvider.GetCldb());
        classPackage.Read(cldbReader);

        _classDatabase = classPackage.GetClassDatabase(_globalGameManagers.Metadata.UnityVersion);

        _templateGenerator = new Cpp2IlTempGenerator(dataProvider.GetGlobalMetadata(), dataProvider.GetIl2CppBinary());
        _templateGenerator.SetUnityVersion(new UnityVersion(_globalGameManagers.Metadata.UnityVersion));
        _templateGenerator.InitializeCpp2IL();
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
            _globalGameManagers.Close();
            _templateGenerator.Dispose();
        }
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
                throw new InvalidDataException($"Failed to build template for type {info.TypeId}");

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
        var scriptIndex = info.GetScriptIndex(file);

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

            if (scriptPtr.IsNull())
                goto OutAndReset;

            // 确定 MonoScript 所在的文件
            AssetsFile monoScriptFile;
            if (scriptPtr.FileId == 0)
                monoScriptFile = file;
            else if (scriptPtr.FileId == 1)
                monoScriptFile = _globalGameManagers;
            else
                throw new InvalidDataException("Unsupported MonoScript FileID");

            var monoInfo = monoScriptFile.GetAssetInfo(scriptPtr.PathId);

            if (monoInfo is null)
                goto OutAndReset;

            if (!GetMonoScriptInfo(monoScriptFile, monoInfo, out var assemblyName, out var nameSpace,
                    out var className))
                goto OutAndReset;

            // 移除 .dll 扩展名
            if (assemblyName!.EndsWith(".dll"))
                assemblyName = assemblyName.Substring(0, assemblyName.Length - 4);

            var newBase = _templateGenerator.GetTemplateField(
                baseField,
                assemblyName,
                nameSpace,
                className,
                new UnityVersion(file.Metadata.UnityVersion));

            if (newBase != null)
                baseField = newBase;

            OutAndReset:
            // 恢复原始位置
            reader.Position = originalPosition;
        }

        return baseField;
    }

    public static uint GetPhiVersion()
    {
        var meta = LibCpp2IlMain.TheMetadata
                   ?? throw new InvalidOperationException("Cpp2Il is not initialized.");

        var assembly = meta.AssemblyDefinitions
                           .FirstOrDefault(a => a.AssemblyName.Name == "Assembly-CSharp")
                       ?? throw new InvalidDataException("Cannot find Assembly-CSharp.");

        var type = assembly.Image.Types?
                       .FirstOrDefault(t => t.FullName == "Constants")
                   ?? throw new InvalidDataException("Cannot find Constants class.");

        var field = type.Fields?
                        .FirstOrDefault(f => f.Name == "IntVersion")
                    ?? throw new InvalidDataException("Cannot find IntVersion field.");

        var defaultValue = meta.GetFieldDefaultValue(field)?.Value
                           ?? throw new InvalidDataException("There is no default value for the IntVersion field.");

        if (defaultValue is int intValue)
            return (uint)intValue;

        throw new InvalidDataException($"Invalid version type: {defaultValue.GetType()}");
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
            info.GetAbsoluteByteOffset(file));

        if (template == null)
            return false;

        var offset = info.GetAbsoluteByteOffset(file);
        file.Reader.Position = offset;

        RefTypeManager refMan = new();
        refMan.FromTypeTree(file.Metadata);

        var valueField = template.MakeValue(file.Reader, offset, refMan);

        assemblyName = valueField["m_AssemblyName"]?.AsString;
        nameSpace = valueField["m_Namespace"]?.AsString;
        className = valueField["m_ClassName"]?.AsString;

        return !string.IsNullOrEmpty(assemblyName) && !string.IsNullOrEmpty(className) && nameSpace is not null;
    }

    public AssetTypeValueField? TryFindMonoBehaviour(AssetsFile file, string name)
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

            var monoInfo = _globalGameManagers.GetAssetInfo(msId);
            if (monoInfo == null)
                continue;

            var msBase = GetBaseField(_globalGameManagers, monoInfo, false);
            var msName = msBase["m_Name"]?.AsString;

            if (msName == name)
                return GetBaseField(file, info, true);
        }

        return null;
    }

    public AssetTypeValueField FindMonoBehaviour(AssetsFile file, string name)
    {
        return TryFindMonoBehaviour(file, name) ??
               throw new ArgumentException("Requested MonoBehaviour not found in the provided file.", nameof(name));
    }
}