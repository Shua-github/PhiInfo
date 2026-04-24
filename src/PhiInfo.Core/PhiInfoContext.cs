using System;
using PhiInfo.Core.Asset;
using PhiInfo.Core.Info;
using PhiInfo.Core.Type;

namespace PhiInfo.Core;

public class PhiInfoContext : IDisposable
{
    private readonly IDataProvider _dataProvider;
    private bool _disposed;

    public PhiInfoContext(IDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
        Field = new FieldProvider(dataProvider);
        Info = new InfoProvider(dataProvider, Field);
        Asset = new AssetProvider(dataProvider);
    }

    public AssetProvider Asset { get; }
    public InfoProvider Info { get; }
    public FieldProvider Field { get; }

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
            Field.Dispose();
            _dataProvider.Dispose();
        }
    }
}