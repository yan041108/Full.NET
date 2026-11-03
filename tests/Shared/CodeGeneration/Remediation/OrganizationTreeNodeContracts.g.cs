#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Acme.Remediation.Generated;

public static class OrganizationTreeNodePermissions
{
    /// <summary>允许读取实体列表与详情。</summary>
    public const string Read = "catalog.organization_tree_nodes.read";
    /// <summary>允许创建实体。</summary>
    public const string Create = "catalog.organization_tree_nodes.create";
    /// <summary>允许更新实体。</summary>
    public const string Update = "catalog.organization_tree_nodes.update";
    /// <summary>允许停用或删除实体。</summary>
    public const string Disable = "catalog.organization_tree_nodes.disable";
}

public sealed record OrganizationTreeNodeResponse(
    [property: JsonPropertyName("id"), JsonRequired] Guid Id,
    [property: JsonPropertyName("tenantId"), JsonRequired] Guid TenantId,
    [property: JsonPropertyName("organizationUnitId"), JsonRequired] Guid OrganizationUnitId,
    [property: JsonPropertyName("name"), JsonRequired] string Name,
    [property: JsonPropertyName("parentId")] Guid? ParentId,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version,
    [property: JsonPropertyName("isDeleted"), JsonRequired] bool IsDeleted,
    [property: JsonPropertyName("deletedAtUtc"), JsonConverter(typeof(Full.NET.Hosting.Serialization.NullableUtcDateTimeJsonConverter))] DateTimeOffset? DeletedAtUtc,
    [property: JsonPropertyName("deletedById")] Guid? DeletedById);

public sealed record CreateOrganizationTreeNodeRequest(
    [property: JsonPropertyName("name"), JsonRequired] string Name,
    [property: JsonPropertyName("parentId")] Guid? ParentId);

public sealed record UpdateOrganizationTreeNodeRequest(
    [property: JsonPropertyName("name"), JsonRequired] string Name,
    [property: JsonPropertyName("parentId")] Guid? ParentId,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public sealed record DeleteOrganizationTreeNodeRequest(
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public static class OrganizationTreeNodeErrorCodes
{
    public const string NotFound =
        "catalog.organization_tree_nodes.not_found";

    public const string VersionConflict =
        "catalog.organization_tree_nodes.version_conflict";

    public const string InvalidParent =
        "catalog.organization_tree_nodes.invalid_parent";

    public const string ParentCycle =
        "catalog.organization_tree_nodes.parent_cycle";
}


/// <summary>仅开放 Schema 声明的过滤和排序字段；空过滤不参与条件，Id 为稳定分页尾键。</summary>
public sealed class OrganizationTreeNodeListQuery
{
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "sortBy")] public string? SortBy { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "descending")] public bool? Descending { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterName")] public string? FilterName { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterParentId")] public Guid? FilterParentId { get; init; }

    internal static readonly System.Collections.Generic.HashSet<string> QueryNames = new(System.StringComparer.Ordinal)
    { "page", "pageSize", "sortBy", "descending", "filterName", "filterParentId" };

    internal Full.NET.Abstractions.Results.Error? Validate() =>
        SortBy is not null && SortBy is not ("id" or "name" or "parentId")
            || FilterName?.Length > 64
            || FilterParentId == Guid.Empty ? Invalid() : null;

    internal static Full.NET.Abstractions.Results.Error Invalid() => new("catalog.organization_tree_nodes.invalid_query",
        "The generated resource query is invalid.", Full.NET.Abstractions.Results.ErrorType.Validation);

    internal static bool IsWireValueValid(string name, string? value) => name switch
    {

        _ => true,
    };

    internal System.Collections.Generic.Dictionary<string, object?> Parameters(long offset, int pageSize) => new()
    {
        ["Offset"] = offset, ["PageSize"] = pageSize, ["CodegenSortBy"] = SortBy ?? "id", ["CodegenDescending"] = Descending ?? false,
            ["Query_Name"] = Pattern(FilterName),
            ["Query_ParentId"] = FilterParentId
    };

    // LIKE 转义同时覆盖两库通配符与 SQL Server 方括号，用户输入始终是字面量。
    private static string? Pattern(string? value) => value is null ? null : "%" + value.Replace("~", "~~", StringComparison.Ordinal)
        .Replace("%", "~%", StringComparison.Ordinal).Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal) + "%";
}
