using System;
using PhiInfo.Core.Type;

namespace PhiInfo.Core;

public class PhiInfoContext : IDisposable
{
    private readonly Lazy<CatalogProvider> _catalog;
    private readonly IDataProvider _dataProvider;

    private readonly FieldProvider _fieldProvider;
    private readonly bool _initialized;
    private bool _disposed;

    public PhiInfoContext(IDataProvider dataProvider, Language language = Language.Chinese)
    {
        _dataProvider = dataProvider;
        Language = language;
        _fieldProvider = new FieldProvider(dataProvider);
        Info = new InfoProvider(dataProvider, _fieldProvider, language);
        _catalog = new Lazy<CatalogProvider>(() => new CatalogProvider(dataProvider));
        Bundle = new BundleProvider(dataProvider);
        _initialized = true;
    }

    public BundleProvider Bundle { get; }
    public InfoProvider Info { get; }
    public CatalogProvider Catalog => _catalog.Value;

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
            _fieldProvider.Dispose();
            _dataProvider.Dispose();
        }
    }
}