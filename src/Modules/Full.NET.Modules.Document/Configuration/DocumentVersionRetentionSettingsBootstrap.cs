using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Full.NET.Modules.Document.Configuration;

/// <summary>启动时加载数据库中的 Host 保留策略覆盖。</summary>
internal sealed class DocumentVersionRetentionSettingsBootstrap(
    IServiceScopeFactory scopeFactory,
    DocumentVersionRetentionSettingsStore store,
    DocumentVersionRetentionOptionsChangeTokenSource changeTokenSource,
    ILogger<DocumentVersionRetentionSettingsBootstrap> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ReloadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            DocumentVersionRetentionSettingsBootstrapLog.LoadFailed(logger, exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var tenantWriter = scope.ServiceProvider.GetRequiredService<ICurrentTenantContextWriter>();
        tenantWriter.SetHost();
        var repository = scope.ServiceProvider.GetRequiredService<DocumentVersionRetentionSettingRepository>();
        var record = await repository.GetHostAsync(cancellationToken).ConfigureAwait(false);
        store.SetHostOverride(record);
        changeTokenSource.SignalChange();
    }
}

internal static partial class DocumentVersionRetentionSettingsBootstrapLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "无法从数据库加载文档版本保留策略覆盖，将仅使用 appsettings。")]
    public static partial void LoadFailed(ILogger logger, Exception exception);
}
