using System.IO;

namespace PhiInfo.Core.Asset;

public interface IAssetDataProvider
{
    Stream GetCatalog();
    Stream GetBundle(string name);
}