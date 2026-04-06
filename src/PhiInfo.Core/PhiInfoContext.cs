using System;
using PhiInfo.Core.Type;

namespace PhiInfo.Core;

public class PhiInfoContext : IDisposable
{
    private readonly IDataProvider _dataProvider;
    private readonly bool _initialized;
    private bool _disposed;

    public PhiInfoContext(IDataProvider dataProvider, Language language = Language.Chinese)
    {
        Language = language;
        _dataProvider = dataProvider;
        var field = new FieldProvider(dataProvider);
        Info = new InfoProvider(dataProvider, field, language);
        Catalog = new CatalogProvider(dataProvider);
        Asset = new AssetProvider(dataProvider);
        _initialized = true;
    }

    public AssetProvider Asset { get; }
    public InfoProvider Info { get; }
    public CatalogProvider Catalog { get; }

    public Language Language
    {
        get;
        set
        {
            field = value;
            if (_initialized) Info.Language = value;
        }
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
            Info.Dispose();
            _dataProvider.Dispose();
        }
    }
}