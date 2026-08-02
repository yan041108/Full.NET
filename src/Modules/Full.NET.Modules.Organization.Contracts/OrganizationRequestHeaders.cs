namespace Full.NET.Modules.Organization.Contracts;

/// <summary>组织模块约定的受信 HTTP 头；值由服务端解析，不得进入客户端可写 JSON 契约。</summary>
public static class OrganizationRequestHeaders
{
    /// <summary>Create 组织归属实体时绑定目标机构单元 Id 的受信头。</summary>
    public const string OrganizationUnitId = "X-FullNet-Organization-Unit-Id";
}