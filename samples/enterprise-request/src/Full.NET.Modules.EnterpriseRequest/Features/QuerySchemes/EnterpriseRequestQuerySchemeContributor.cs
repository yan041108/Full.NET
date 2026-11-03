using Full.NET.Modules.EnterpriseRequest.Generated;
using Full.NET.Modules.Settings.Contracts;

namespace Full.NET.Modules.EnterpriseRequest.Features.QuerySchemes;

/// <summary>复用业务列表实际支持的包含筛选和稳定排序；Settings 不持有业务实现或 SQL。</summary>
internal sealed class EnterpriseRequestQuerySchemeContributor : IQuerySchemeContributor
{
    public IReadOnlyCollection<QuerySchemeDefinition> Definitions { get; } =
    [
        new("enterprise_request.enterprise_requests", 1, EnterpriseRequestPermissions.Read, false, true,
            [new("requestNumber", 64), new("title", 200), new("status", 32)],
            ["id", "requestNumber", "title", "status"],
            ["requestNumber", "title", "status", "totalAmount", "applicantUserId"]),
    ];
}
