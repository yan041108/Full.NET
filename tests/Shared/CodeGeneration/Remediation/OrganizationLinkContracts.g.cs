#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Acme.Remediation.Generated;

public static class OrganizationLinkPermissions
{
    /// <summary>允许读取实体列表与详情。</summary>
    public const string Read = "catalog.organization_links.read";
    /// <summary>允许创建实体。</summary>
    public const string Create = "catalog.organization_links.create";
    /// <summary>允许更新实体。</summary>
    public const string Update = "catalog.organization_links.update";
    /// <summary>允许停用或删除实体。</summary>
    public const string Disable = "catalog.organization_links.disable";
}

public sealed record OrganizationLinkResponse(
    [property: JsonPropertyName("id"), JsonRequired] Guid Id,
    [property: JsonPropertyName("tenantId"), JsonRequired] Guid TenantId,
    [property: JsonPropertyName("organizationUnitId"), JsonRequired] Guid OrganizationUnitId,
    [property: JsonPropertyName("sourceId"), JsonRequired] Guid SourceId,
    [property: JsonPropertyName("targetId"), JsonRequired] Guid TargetId,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version,
    [property: JsonPropertyName("isDeleted"), JsonRequired] bool IsDeleted,
    [property: JsonPropertyName("deletedAtUtc"), JsonConverter(typeof(Full.NET.Hosting.Serialization.NullableUtcDateTimeJsonConverter))] DateTimeOffset? DeletedAtUtc,
    [property: JsonPropertyName("deletedById")] Guid? DeletedById);

public sealed record CreateOrganizationLinkRequest(
    [property: JsonPropertyName("sourceId"), JsonRequired] Guid SourceId,
    [property: JsonPropertyName("targetId"), JsonRequired] Guid TargetId);

public sealed record UpdateOrganizationLinkRequest(
    [property: JsonPropertyName("sourceId"), JsonRequired] Guid SourceId,
    [property: JsonPropertyName("targetId"), JsonRequired] Guid TargetId,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public sealed record DeleteOrganizationLinkRequest(
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public static class OrganizationLinkErrorCodes
{
    public const string NotFound =
        "catalog.organization_links.not_found";

    public const string VersionConflict =
        "catalog.organization_links.version_conflict";

    public const string UniqueConflict =
        "catalog.organization_links.unique_conflict";

    public const string PrincipalNotFound =
        "catalog.organization_links.principal_not_found";
}


/// <summary>仅开放 Schema 声明的过滤和排序字段；空过滤不参与条件，Id 为稳定分页尾键。</summary>
public sealed class OrganizationLinkListQuery
{
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "sortBy")] public string? SortBy { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "descending")] public bool? Descending { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterSourceId")] public Guid? FilterSourceId { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterTargetId")] public Guid? FilterTargetId { get; init; }

    internal static readonly System.Collections.Generic.HashSet<string> QueryNames = new(System.StringComparer.Ordinal)
    { "page", "pageSize", "sortBy", "descending", "filterSourceId", "filterTargetId" };

    internal Full.NET.Abstractions.Results.Error? Validate() =>
        SortBy is not null && SortBy is not ("id" or "sourceId" or "targetId")
            || FilterSourceId == Guid.Empty
            || FilterTargetId == Guid.Empty ? Invalid() : null;

    internal static Full.NET.Abstractions.Results.Error Invalid() => new("catalog.organization_links.invalid_query",
        "The generated resource query is invalid.", Full.NET.Abstractions.Results.ErrorType.Validation);

    internal static bool IsWireValueValid(string name, string? value) => name switch
    {

        _ => true,
    };

    internal System.Collections.Generic.Dictionary<string, object?> Parameters(long offset, int pageSize) => new()
    {
        ["Offset"] = offset, ["PageSize"] = pageSize, ["CodegenSortBy"] = SortBy ?? "id", ["CodegenDescending"] = Descending ?? false,
            ["Query_SourceId"] = FilterSourceId,
            ["Query_TargetId"] = FilterTargetId
    };

    // LIKE 转义同时覆盖两库通配符与 SQL Server 方括号，用户输入始终是字面量。
    private static string? Pattern(string? value) => value is null ? null : "%" + value.Replace("~", "~~", StringComparison.Ordinal)
        .Replace("%", "~%", StringComparison.Ordinal).Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal) + "%";
}


/// <summary>关联读取权限下的最小候选投影，不暴露主端完整 DTO。</summary>
public sealed record OrganizationLinkRelationOption([property: JsonRequired] Guid Value, [property: JsonRequired] string Label);
