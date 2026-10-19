using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

/// <summary>将配置节绑定到强类型 Options。</summary>
namespace Full.NET.Modules.Document.Configuration;

internal sealed class DocumentVersionRetentionOptionsPostConfigurer(
    DocumentVersionRetentionSettingsStore store)
    : IPostConfigureOptions<DocumentVersionRetentionOptions>
{
    public void PostConfigure(string? name, DocumentVersionRetentionOptions options) =>
        store.ApplyOverrides(options);
}

internal sealed class DocumentVersionRetentionOptionsChangeTokenSource
    : IOptionsChangeTokenSource<DocumentVersionRetentionOptions>, IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource _source = new();

    public string Name => Options.DefaultName;

    public IChangeToken GetChangeToken()
    {
        lock (_gate)
        {
            return new CancellationChangeToken(_source.Token);
        }
    }

    public void SignalChange()
    {
        lock (_gate)
        {
            _source.Cancel();
            _source.Dispose();
            _source = new CancellationTokenSource();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _source.Dispose();
        }
    }
}
