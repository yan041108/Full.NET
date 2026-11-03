using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Document.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Document.Features.ManageHostDocumentShares;

/// <summary>会话过期后保留一天的幂等窗口，Worker 每轮有界清理。</summary>
internal sealed class DocumentShareSessionCleanup(IServiceScopeFactory scopes, ILogger<DocumentShareSessionCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var provider = scope.ServiceProvider;
                var tenant = provider.GetRequiredService<ICurrentTenantContextWriter>();
                tenant.SetHost();
                try
                {
                    var before = provider.GetRequiredService<IClock>().UtcNow.AddHours(-24);
                    var parameters = DocumentSqlParameters.Create(("Before", before));
                    var query = provider.GetRequiredService<IQueryExecutor>();
                    var database = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Provider;
                    var ids = await query.QueryAsync<Guid>(database == DatabaseProvider.MySql
                        ? DocumentShareSessionSql.ExpiredMySql : DocumentShareSessionSql.ExpiredSqlServer, parameters, stoppingToken).ConfigureAwait(false);
                    var commands = provider.GetRequiredService<ICommandExecutor>();
                    foreach (var id in ids)
                        await commands.ExecuteAsync(DocumentShareSessionSql.DeleteExpired,
                            DocumentSqlParameters.Create(("Id", id), ("Before", before)), stoppingToken).ConfigureAwait(false);
                }
                finally { tenant.Clear(); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { DocumentShareSessionCleanupLog.Failed(logger, exception); }
        }
    }
}

internal static partial class DocumentShareSessionCleanupLog
{
    [LoggerMessage(EventId = 4731, Level = LogLevel.Error, Message = "Document share session cleanup failed")]
    public static partial void Failed(ILogger logger, Exception exception);
}
