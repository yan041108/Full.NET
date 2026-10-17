using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

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
    private bool _disposed;

    public string Name => Options.DefaultName;

    public IChangeToken GetChangeToken()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new CancellationChangeToken(_source.Token);
        }
    }

    public void SignalChange()
    {
        CancellationTokenSource previous;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            previous = _source;
            // 监视器在回调中取得下一代令牌，必须先发布新源，避免重新订阅已取消令牌。
            _source = new CancellationTokenSource();
        }

        try
        {
            // 回调可能跨线程重入本源；不能在持锁状态执行外部监听者。
            previous.Cancel();
        }
        finally
        {
            previous.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _source.Dispose();
        }
    }
}

/// <summary>以无状态适配器复用唯一通知源，同时保持可重复模块注册与配置绑定通知并存。</summary>
internal sealed class DocumentVersionRetentionOptionsChangeTokenSourceAdapter(
    DocumentVersionRetentionOptionsChangeTokenSource source)
    : IOptionsChangeTokenSource<DocumentVersionRetentionOptions>
{
    public string Name => source.Name;

    public IChangeToken GetChangeToken() => source.GetChangeToken();
}
