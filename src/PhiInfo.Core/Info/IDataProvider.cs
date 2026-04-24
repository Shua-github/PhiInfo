using System.IO;

namespace PhiInfo.Core.Info;

public interface IFieldDataProvider
{
    Stream GetCldb();
    Stream GetGlobalGameManagers();
    byte[] GetIl2CppBinary();
    byte[] GetGlobalMetadata();
}

public interface IInfoDataProvider
{
    Stream GetLevel0();
    Stream GetLevel22();
}