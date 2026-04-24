using System;
using System.IO;
using System.Linq;
using AssetsTools.NET;
using AssetsTools.NET.Cpp2IL;
using AssetsTools.NET.Extra;
using LibCpp2IL;
using LibCpp2IL.Logging;

namespace PhiInfo.Core.Info;

internal class LibLogWriter : LogWriter
{
    public override void Error(string message)
    {
    }

    public override void Info(string message)
    {
    }

    public override void Verbose(string message)
    {
    }

    public override void Warn(string message)
    {
    }
}

public class FieldProvider : IDisposable
{
    private readonly Lazy<ClassDatabaseFile> _classDatabase;
    private readonly Lazy<AssetsFile> _globalGameManagers;
    private readonly Lazy<Cpp2IlTempGenerator> _templateGenerator;

    private bool _disposed;

    public FieldProvider(IFieldDataProvider dataProvider)
    {
        LibLogger.Writer = new LibLogWriter();

        _globalGameManagers = new Lazy<AssetsFile>(() =>
        {
            var file = new AssetsFile();
            file.Read(new AssetsFileReader(dataProvider.GetGlobalGameManagers()));
            return file;
        });

        _classDatabase = new Lazy<ClassDatabaseFile>(() =>
        {
            ClassPackageFile classPackage = new();
            using AssetsFileReader cldbReader = new(dataProvider.GetCldb());
            classPackage.Read(cldbReader);
            return classPackage.GetClassDatabase(_globalGameManagers.Value.Metadata.UnityVersion);
        });

        _templateGenerator = new Lazy<Cpp2IlTempGenerator>(() =>
        {
            var generator = new Cpp2IlTempGenerator(dataProvider.GetGlobalMetadata(), dataProvider.GetIl2CppBinary());
            generator.SetUnityVersion(new UnityVersion(_globalGameManagers.Value.Metadata.UnityVersion));
            generator.InitializeCpp2IL();
            return generator;
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
            if (_globalGameManagers.IsValueCreated) _globalGameManagers.Value.Close();

            if (_templateGenerator.IsValueCreated) _templateGenerator.Value.Dispose();
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
            var cldbType = _classDatabase.Value.FindAssetClassByID(info.TypeId);
            if (cldbType == null)
                return null;

            baseField = new AssetTypeTemplateField();
            baseField.FromClassDatabase(_classDatabase.Value, cldbType);
        }

        // 3. MonoBehaviour: 使用 MonoTempGenerator 补充字段
        if (info.TypeId == (int)AssetClassID.MonoBehaviour && monoFields && reader != null)
        {
            var originalPosition = reader.Position;
            reader.Position = absByteStart;

            RefTypeManager tempRefMan = new();
            tempRefMan.FromTypeTree(file.Metadata);

            var mbBase = baseField.MakeValue(reader, absByteStart, tempRefMan);
            var scriptPtr = AssetPPtr.FromField(mbBase["m_Script"]);

            if (scriptPtr.IsNull())
                goto OutAndReset;

            AssetsFile monoScriptFile;
            if (scriptPtr.FileId == 0)
                monoScriptFile = file;
            else if (scriptPtr.FileId == 1)
                monoScriptFile = _globalGameManagers.Value;
            else
                throw new InvalidDataException("Unsupported MonoScript FileID");

            var monoInfo = monoScriptFile.GetAssetInfo(scriptPtr.PathId);

            if (monoInfo is null)
                goto OutAndReset;

            if (!GetMonoScriptInfo(monoScriptFile, monoInfo, out var assemblyName, out var nameSpace,
                    out var className))
                goto OutAndReset;

            if (assemblyName!.EndsWith(".dll"))
                assemblyName = assemblyName.Substring(0, assemblyName.Length - 4);

            var newBase = _templateGenerator.Value.GetTemplateField(
                baseField,
                assemblyName,
                nameSpace,
                className,
                new UnityVersion(file.Metadata.UnityVersion));

            if (newBase != null)
                baseField = newBase;

            OutAndReset:
            reader.Position = originalPosition;
        }

        return baseField;
    }

    public PhiVersion GetPhiVersion()
    {
        _ = _templateGenerator.Value;
        var meta = LibCpp2IlMain.TheMetadata!;

        var assembly = meta.AssemblyDefinitions
                           .FirstOrDefault(a => a.AssemblyName.Name == "Assembly-CSharp")
                       ?? throw new InvalidDataException("Cannot find Assembly-CSharp.");

        var type = assembly.Image.Types?
                       .FirstOrDefault(t => t.FullName == "Constants")
                   ?? throw new InvalidDataException("Cannot find Constants class.");

        var codeField = type.Fields?
                            .FirstOrDefault(f => f.Name == "IntVersion")
                        ?? throw new InvalidDataException("Cannot find IntVersion field.");

        var codeDefaultValue = meta.GetFieldDefaultValue(codeField)?.Value
                               ?? throw new InvalidDataException("There is no default value for the IntVersion field.");

        var nameField = type.Fields?
                            .FirstOrDefault(f => f.Name == "Version")
                        ?? throw new InvalidDataException("Cannot find Version field.");

        var nameDefaultValue = meta.GetFieldDefaultValue(nameField)?.Value
                               ?? throw new InvalidDataException("There is no default value for the Version field.");

        if (codeDefaultValue is int intValue && nameDefaultValue is string stringValue)
            return new PhiVersion((uint)intValue, stringValue);

        throw new InvalidDataException(
            $"Invalid version type: {nameDefaultValue.GetType()} and {codeDefaultValue.GetType()}");
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

            var monoInfo = _globalGameManagers.Value.GetAssetInfo(msId);
            if (monoInfo == null)
                continue;

            var msBase = GetBaseField(_globalGameManagers.Value, monoInfo, false);
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