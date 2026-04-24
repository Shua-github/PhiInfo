using System;
using System.Collections.Generic;

namespace PhiInfo.Core.Asset;

public class AssetProvider(IAssetDataProvider dataProvider)
{
    private readonly Lazy<Dictionary<string, string>> _catalog
        = new(() =>
            CatalogParser.Parse(dataProvider.GetCatalog())
        );

    public Dictionary<string, string> Catalog => _catalog.Value;

    public T Get<T>(string name)
        where T : UnityAsset, new()
    {
        var obj = new T();
        obj.Init(dataProvider.GetBundle(name));
        return obj;
    }
}