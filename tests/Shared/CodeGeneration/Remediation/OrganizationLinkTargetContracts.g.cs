#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Acme.Remediation.Generated;

public static class OrganizationLinkTargetPermissions
{
    /// <summary>允许读取实体列表与详情。</summary>
    public const string Read = "catalog.organization_targets.read";
    /// <summary>允许创建实体。</summary>
    public const string Create = "catalog.organization_targets.create";
    /// <summary>允许更新实体。</summary>
    public const string Update = "catalog.organization_targets.update";
    /// <summary>允许停用或删除实体。</summary>
    public const string Disable = "catalog.organization_targets.disable";
}

public sealed record OrganizationLinkTargetResponse(
    [property: JsonPropertyName("id"), JsonRequired] Guid Id,
    [property: JsonPropertyName("tenantId"), JsonRequired] Guid TenantId,
    [property: JsonPropertyName("organizationUnitId"), JsonRequired] Guid OrganizationUnitId,
    [property: JsonPropertyName("name"), JsonRequired] string Name,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version,
    [property: JsonPropertyName("isDeleted"), JsonRequired] bool IsDeleted,
    [property: JsonPropertyName("deletedAtUtc"), JsonConverter(typeof(Full.NET.Hosting.Serialization.NullableUtcDateTimeJsonConverter))] DateTimeOffset? DeletedAtUtc,
    [property: JsonPropertyName("deletedById")] Guid? DeletedById);

public sealed record CreateOrganizationLinkTargetRequest(
    [property: JsonPropertyName("name"), JsonRequired] string Name);

public sealed record UpdateOrganizationLinkTargetRequest(
    [property: JsonPropertyName("name"), JsonRequired] string Name,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public sealed record DeleteOrganizationLinkTargetRequest(
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public static class OrganizationLinkTargetErrorCodes
{
    public const string NotFound =
        "catalog.organization_targets.not_found";

    public const string VersionConflict =
        "catalog.organization_targets.version_conflict";
}


/// <summary>仅开放 Schema 声明的过滤和排序字段；空过滤不参与条件，Id 为稳定分页尾键。</summary>
public sealed class OrganizationLinkTargetListQuery
{
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "sortBy")] public string? SortBy { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "descending")] public bool? Descending { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterId")] public Guid? FilterId { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterOrganizationUnitId")] public Guid? FilterOrganizationUnitId { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterName")] public string? FilterName { get; init; }

    internal static readonly System.Collections.Generic.HashSet<string> QueryNames = new(System.StringComparer.Ordinal)
    { "page", "pageSize", "sortBy", "descending", "filterId", "filterOrganizationUnitId", "filterName" };

    internal Full.NET.Abstractions.Results.Error? Validate() =>
        SortBy is not null && SortBy is not ("id" or "name")
            || FilterName?.Length > 64
            || FilterId == Guid.Empty
            || FilterOrganizationUnitId == Guid.Empty ? Invalid() : null;

    internal static Full.NET.Abstractions.Results.Error Invalid() => new("catalog.organization_targets.invalid_query",
        "The generated resource query is invalid.", Full.NET.Abstractions.Results.ErrorType.Validation);

    internal static bool IsWireValueValid(string name, string? value) => name switch
    {

        _ => true,
    };

    internal System.Collections.Generic.Dictionary<string, object?> Parameters(long offset, int pageSize) => new()
    {
        ["Offset"] = offset, ["PageSize"] = pageSize, ["CodegenSortBy"] = SortBy ?? "id", ["CodegenDescending"] = Descending ?? false,
            ["Query_Id"] = FilterId,
            ["Query_OrganizationUnitId"] = FilterOrganizationUnitId,
            ["Query_Name"] = Pattern(FilterName)
    };

    // LIKE 转义同时覆盖两库通配符与 SQL Server 方括号，用户输入始终是字面量。
    private static string? Pattern(string? value) => value is null ? null : "%" + value.Replace("~", "~~", StringComparison.Ordinal)
        .Replace("%", "~%", StringComparison.Ordinal).Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal) + "%";
}
