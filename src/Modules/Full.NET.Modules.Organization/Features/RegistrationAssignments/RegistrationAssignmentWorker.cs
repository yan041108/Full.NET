using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.Features.RegistrationAssignments;

/// <summary>每批最多二十项；未领取意图可在进程退出后继续，本地事务防止半分配被记为完成。</summary>
internal sealed class RegistrationAssignmentWorker(IServiceScopeFactory scopes,
    IOptions<DatabaseOptions> options, ILogger<RegistrationAssignmentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunBatchAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogError(error, "Organization registration assignment recovery failed."); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task RunBatchAsync(CancellationToken token)
    {
        await using var scope = scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var context = provider.GetRequiredService<ICurrentTenantContextWriter>();
        context.SetHost();
        try
        {
            var clock = provider.GetRequiredService<IClock>();
            var rows = await provider.GetRequiredService<IQueryExecutor>().QueryAsync<RegistrationAssignmentRecord>(
                options.Value.Provider == DatabaseProvider.SqlServer ? RegistrationAssignmentSql.DueSqlServer : RegistrationAssignmentSql.DueMySql,
                OrganizationSqlParameters.Create(("Now", clock.UtcNow)), token).ConfigureAwait(false);
            foreach (var row in rows)
            {
                context.SetHost();
                var active = await provider.GetRequiredService<IActiveTenantContextResolver>().ResolveActiveByIdAsync(row.TenantId, token).ConfigureAwait(false);
                // 目标取自已验证事件落库后的组织工作项；非活动租户只终止本地意图，不新增组织关系。
                context.SetTenant(active ?? new TenantContext(row.TenantId, row.TenantId.ToString("D"), row.TenantId.ToString("D")));
                var processor = provider.GetRequiredService<RegistrationAssignmentProcessor>();
                try { await processor.ProcessAsync(row, active is not null, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception error)
                {
                    var code = error.Message is "organization.registration_assignment.unit_unavailable" or "organization.registration_assignment.position_unavailable"
                        ? error.Message : "organization.registration_assignment.retry_required";
                    await processor.RetryAsync(row, code, token).ConfigureAwait(false);
                    logger.LogWarning(error, "Organization registration assignment {OperationId} is pending retry.", row.Id);
                }
            }
        }
        finally { context.Clear(); }
    }
}
