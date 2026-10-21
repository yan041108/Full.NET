using Full.NET.Abstractions.Tenancy;
using Full.NET.Abstractions.Messaging;
using Full.NET.Data.Abstractions;
using Full.NET.Messaging.Abstractions;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Organization.Features.ManageTenantUserUnits;

/// <summary>有界恢复历史及新隶属版本；标记与 Outbox 在同一源模块事务中提交。</summary>
internal sealed class UserUnitProjectionPublicationHostedService(
    IServiceScopeFactory scopes, IOptions<DatabaseOptions> options,
    ILogger<UserUnitProjectionPublicationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Guid? after = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var provider = scope.ServiceProvider;
                var context = provider.GetRequiredService<ICurrentTenantContextWriter>();
                context.SetHost();
                try
                {
                var rows = await provider.GetRequiredService<IQueryExecutor>().QueryAsync<OrganizationUserUnitRecord>(
                    options.Value.Provider == DatabaseProvider.MySql ? FindMySql : FindSqlServer,
                    OrganizationSqlParameters.Create(("AfterId", after)), stoppingToken).ConfigureAwait(false);
                var units = rows.Count == 0 ? new Dictionary<(Guid, Guid), OrganizationUnitRecord>()
                    : (await provider.GetRequiredService<IQueryExecutor>().QueryAsync<OrganizationUnitRecord>(FindUnits,
                        OrganizationSqlParameters.Create(("AssignmentIds", rows.Select(row => row.Id).ToArray())), stoppingToken)
                        .ConfigureAwait(false)).ToDictionary(unit => (unit.TenantId, unit.Id));
                var publishedUnits = new HashSet<(Guid, Guid)>();
                foreach (var row in rows)
                {
                    after = row.Id;
                    // 作用域取自本模块持久化源行；发布失效状态不依赖租户仍处于活动状态。
                    context.SetTenant(new TenantContext(row.TenantId, row.TenantId.ToString("D"), row.TenantId.ToString("D")));
                    try
                    {
                        var published = await provider.GetRequiredService<ICommandTransaction>().ExecuteAsync(async token =>
                        {
                            var changed = await provider.GetRequiredService<ICommandExecutor>().ExecuteAsync(Mark,
                                OrganizationSqlParameters.Create(("AssignmentId", row.Id), ("Version", row.Version)), token).ConfigureAwait(false);
                            if (changed == 1)
                            {
                                if (!publishedUnits.Contains((row.TenantId, row.UnitId)) && units.TryGetValue((row.TenantId, row.UnitId), out var unit))
                                    await provider.GetRequiredService<IOutboxWriter>().AddAsync(
                                        IdentityOrganizationUnitProjectionIntegrationEventTypes.UnitChanged, 1,
                                        new IdentityOrganizationUnitChangedIntegrationEvent(unit.TenantId, unit.Id, unit.Name,
                                            unit.IsActive, unit.Version, unit.UpdatedAtUtc ?? unit.CreatedAtUtc),
                                        IntegrationEventMetadata.Create(partitionKey: row.TenantId.ToString("D"), producer: "fullnet.organization"), token).ConfigureAwait(false);
                                await provider.GetRequiredService<IOutboxWriter>().AddAsync(
                                    IdentityOrganizationUserUnitEventTypes.Changed, 1,
                                    new IdentityOrganizationUserUnitChangedIntegrationEvent(row.TenantId, row.Id, row.UserId,
                                        row.UnitId, row.IsActive, row.Version, row.UpdatedAtUtc ?? row.CreatedAtUtc),
                                    IntegrationEventMetadata.Create(partitionKey: row.TenantId.ToString("D"), producer: "fullnet.organization"), token).ConfigureAwait(false);
                            }
                            return changed == 1;
                        }, stoppingToken).ConfigureAwait(false);
                        if (published) publishedUnits.Add((row.TenantId, row.UnitId));
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception exception) { logger.LogError(exception, "Organization user-unit projection publication failed."); }
                }
                if (rows.Count == 100) continue;
                after = null;
                }
                finally { context.Clear(); }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception exception) { logger.LogError(exception, "Organization user-unit projection recovery failed."); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        }
    }
    internal static readonly SqlStatement FindSqlServer = new("organization.user_unit_projection.find_unpublished.sql_server", """
        SELECT TOP (100) Id,TenantId,UserId,UnitId,IsPrimary,IsActive,CreatedAtUtc,UpdatedAtUtc,Version
        FROM fn_organization_user_unit
        WHERE PublishedProjectionVersion < Version AND (@AfterId IS NULL OR Id > @AfterId)
        ORDER BY Id
        """, SqlDataScope.Global);
    internal static readonly SqlStatement FindMySql = new("organization.user_unit_projection.find_unpublished.mysql", """
        SELECT Id,TenantId,UserId,UnitId,IsPrimary,IsActive,CreatedAtUtc,UpdatedAtUtc,Version
        FROM fn_organization_user_unit
        WHERE PublishedProjectionVersion < Version AND (@AfterId IS NULL OR Id > @AfterId)
        ORDER BY Id LIMIT 100
        """, SqlDataScope.Global);
    internal static readonly SqlStatement FindUnits = new("organization.user_unit_projection.find_source_units", """
        SELECT DISTINCT unit.Id,unit.TenantId,unit.ParentId,unit.Code,unit.Name,unit.DisplayOrder,
          unit.IsActive,unit.CreatedAtUtc,unit.UpdatedAtUtc,unit.Version
        FROM fn_organization_unit unit
        JOIN fn_organization_user_unit assignment ON assignment.TenantId=unit.TenantId AND assignment.UnitId=unit.Id
        WHERE assignment.Id IN @AssignmentIds
        """, SqlDataScope.Global);
    internal static readonly SqlStatement Mark = new("organization.user_unit_projection.mark_published", """
        UPDATE fn_organization_user_unit SET PublishedProjectionVersion=@Version
        WHERE TenantId=@TenantId AND Id=@AssignmentId AND Version=@Version AND PublishedProjectionVersion < @Version
        """, SqlDataScope.TenantRequired, SqlTenantBinding.CurrentTenantId);
}
