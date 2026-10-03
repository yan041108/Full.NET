using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Organization.Contracts;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Ai.Persistence;
using Full.NET.Modules.Ai.Serialization;
using System.Text.Json;

namespace Full.NET.Modules.Ai.Features;

/// <summary>每次读取完整权威角色与机构关系，再交给 AI 自有 SQL 判断；禁止在本地写事务内调用。</summary>
/// <param name="roles">Identity 当前可信范围的活动普通角色关系。</param>
/// <param name="organizations">Organization 当前可信租户的活动机构及隶属关系。</param>
/// <param name="tenant">可信当前范围，Host 没有租户机构授予。</param>
internal sealed class AiKnowledgeReadAccess(IActiveRoleDirectory roles, ICurrentTenantOrganizationUnitDirectory organizations, ICurrentTenant tenant)
{
    internal async Task<Dictionary<string, object?>> ParametersAsync(Guid actor, CancellationToken token)
    {
        var active = await roles.ListActiveRoleIdsForUserAsync(actor, token).ConfigureAwait(false);
        var units = tenant.IsHost ? Array.Empty<Guid>() : await organizations.ListActiveUnitIdsForUserAsync(actor, token).ConfigureAwait(false);
        // 单个 JSON 集合参数避免合法的大量角色越过 SQL Server 参数上限，两个提供程序均保留完整关系。
        return new Dictionary<string, object?>(StringComparer.Ordinal) { ["OwnerUserId"] = actor,
            ["ActiveRoleIdsJson"] = JsonSerializer.Serialize(active.ToArray(), AiJsonSerializerContext.Default.GuidArray),
            ["ActiveOrganizationUnitIdsJson"] = JsonSerializer.Serialize(units.ToArray(), AiJsonSerializerContext.Default.GuidArray) };
    }
}
