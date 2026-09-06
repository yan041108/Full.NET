using System.Security.Claims;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.Identity.Authorization;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Identity.Persistence;

namespace Full.NET.Modules.Identity.Features.GetHostDashboardSummary;

/// <summary>聚合 Host 工作台指标；跨模块数据只通过所有者实现的只读端口获取。</summary>
internal sealed class HostDashboardQueryService(
    IQueryExecutor queryExecutor,
    IClock clock,
    IEnumerable<IHostDashboardTenantMetricsReader> tenantMetricsReaders,
    IEnumerable<IHostDashboardAuditMetricsReader> auditMetricsReaders,
    IEnumerable<IHostDashboardAuditTrendReader> auditTrendReaders,
    IEnumerable<IHostDashboardWorkflowEntryReader> workflowEntryReaders)
{
    private const int RecentActivityTake = 5;
    private const int TrafficTrendHours = 12;

    /// <summary>
    /// 按当前主体权限聚合工作台摘要；无权片段返回 <see langword="null"/>，不伪造零值。
    /// </summary>
    /// <param name="principal">已验签的当前主体。</param>
    /// <param name="permissionClaims">权限解释器。</param>
    /// <param name="actorUserId">可信当前用户标识。</param>
    /// <param name="cancellationToken">请求取消令牌。</param>
    /// <returns>权限裁剪后的工作台摘要。</returns>
    public async Task<Result<HostDashboardSummaryResponse>> GetSummaryAsync(
        ClaimsPrincipal principal,
        PermissionClaimEvaluator permissionClaims,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(permissionClaims);
        ArgumentOutOfRangeException.ThrowIfEqual(actorUserId, Guid.Empty);

        var now = clock.UtcNow;
        var startOfDayUtc = new DateTimeOffset(
            now.UtcDateTime.Date,
            TimeSpan.Zero);
        var trendFromUtc = now.AddHours(-TrafficTrendHours);

        var canReadTenants = permissionClaims.HasPermission(
            principal,
            HostDashboardMetricPermissions.ActiveTenants);
        var canReadSessions = permissionClaims.HasPermission(
            principal,
            HostDashboardMetricPermissions.OnlineSessions);
        var canReadAuditAccess = permissionClaims.HasPermission(
            principal,
            HostDashboardMetricPermissions.AuditAccess);
        var canReadAuditTrends = permissionClaims.HasPermission(
            principal,
            HostDashboardMetricPermissions.AuditTrends);
        var canReadWorkflowTodos = permissionClaims.HasPermission(
            principal,
            HostDashboardMetricPermissions.WorkflowTodos);
        var canReadWorkflowInstances = permissionClaims.HasPermission(
            principal,
            HostDashboardMetricPermissions.WorkflowInstances);

        var tenantMetricsReader = tenantMetricsReaders.SingleOrDefault();
        var auditMetricsReader = auditMetricsReaders.SingleOrDefault();
        var auditTrendReader = auditTrendReaders.SingleOrDefault();
        var workflowEntryReader = workflowEntryReaders.SingleOrDefault();

        var pendingTasks = new List<Task>();

        Task<long>? activeTenantTask = null;
        if (canReadTenants && tenantMetricsReader is not null)
        {
            activeTenantTask = tenantMetricsReader.CountActiveTenantsAsync(cancellationToken);
            pendingTasks.Add(activeTenantTask);
        }

        Task<long>? onlineSessionTask = null;
        if (canReadSessions)
        {
            onlineSessionTask = queryExecutor.QuerySingleOrDefaultAsync<long>(
                HostDashboardSql.CountActiveHostSessions,
                IdentitySqlParameters.Create(("NowUtc", now)),
                cancellationToken);
            pendingTasks.Add(onlineSessionTask);
        }

        Task<HostDashboardAuditMetrics>? auditMetricsTask = null;
        if (canReadAuditAccess && auditMetricsReader is not null)
        {
            auditMetricsTask = auditMetricsReader.ReadAsync(
                startOfDayUtc,
                RecentActivityTake,
                cancellationToken);
            pendingTasks.Add(auditMetricsTask);
        }

        Task<HostDashboardTrafficTrendResponse>? auditTrendTask = null;
        if (canReadAuditTrends && auditTrendReader is not null)
        {
            auditTrendTask = auditTrendReader.ReadAccessTrendAsync(
                trendFromUtc,
                now,
                cancellationToken);
            pendingTasks.Add(auditTrendTask);
        }

        Task<HostDashboardWorkflowEntryMetrics>? workflowMetricsTask = null;
        if ((canReadWorkflowTodos || canReadWorkflowInstances)
            && workflowEntryReader is not null)
        {
            workflowMetricsTask = workflowEntryReader.ReadAsync(
                actorUserId,
                cancellationToken);
            pendingTasks.Add(workflowMetricsTask);
        }

        if (pendingTasks.Count > 0)
        {
            await Task.WhenAll(pendingTasks).ConfigureAwait(false);
        }

        var auditMetrics = auditMetricsTask is null
            ? null
            : await auditMetricsTask.ConfigureAwait(false);
        var workflowMetrics = workflowMetricsTask is null
            ? null
            : await workflowMetricsTask.ConfigureAwait(false);

        var businessEntries = new List<HostDashboardBusinessEntryResponse>();
        if (canReadWorkflowTodos && workflowMetrics is not null)
        {
            businessEntries.Add(new HostDashboardBusinessEntryResponse(
                HostDashboardBusinessEntryKeys.WorkflowPendingTodos,
                workflowMetrics.PendingTodoCount,
                "/workflow/todos",
                HostDashboardMetricPermissions.WorkflowTodos));
        }

        if (canReadWorkflowInstances && workflowMetrics is not null)
        {
            businessEntries.Add(new HostDashboardBusinessEntryResponse(
                HostDashboardBusinessEntryKeys.WorkflowMyInstances,
                workflowMetrics.MyActiveInstanceCount,
                "/workflow/instances",
                HostDashboardMetricPermissions.WorkflowInstances));
        }

        return Result<HostDashboardSummaryResponse>.Success(
            new HostDashboardSummaryResponse(
                activeTenantTask is null ? null : await activeTenantTask.ConfigureAwait(false),
                onlineSessionTask is null ? null : await onlineSessionTask.ConfigureAwait(false),
                auditMetrics?.TodayRequestCount,
                auditMetrics?.TodayErrorRate,
                auditMetrics?.RecentActivities,
                auditTrendTask is null ? null : await auditTrendTask.ConfigureAwait(false),
                businessEntries.ToArray()));
    }
}
