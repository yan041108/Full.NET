#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Acme.Modules.Catalog.Generated;

public static class ProductPermissions
{
    /// <summary>允许读取实体列表与详情。</summary>
    public const string Read = "catalog.products.read";
    /// <summary>允许创建、更新与停用实体。</summary>
    public const string Write = "catalog.products.write";
}

public sealed record ProductResponse(
    [property: JsonPropertyName("id"), JsonRequired] Guid Id,
    [property: JsonPropertyName("tenantId"), JsonRequired] Guid TenantId,
    [property: JsonPropertyName("displayName"), JsonRequired] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("isActive"), JsonRequired] bool IsActive,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version,
    [property: JsonPropertyName("createdAtUtc"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.UtcDateTimeJsonConverter))] DateTimeOffset CreatedAtUtc);

public sealed record CreateProductRequest(
    [property: JsonPropertyName("displayName"), JsonRequired] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("isActive"), JsonRequired] bool IsActive);

public sealed record UpdateProductRequest(
    [property: JsonPropertyName("displayName"), JsonRequired] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("isActive"), JsonRequired] bool IsActive,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public sealed record DisableProductRequest(
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public static class ProductErrorCodes
{
    public const string NotFound =
        "catalog.products.not_found";

    public const string VersionConflict =
        "catalog.products.version_conflict";
}


/// <summary>仅开放 Schema 声明的过滤和排序字段；空过滤不参与条件，Id 为稳定分页尾键。</summary>
public sealed class ProductListQuery
{
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "sortBy")] public string? SortBy { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "descending")] public bool? Descending { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterName")] public string? FilterName { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterDescription")] public string? FilterDescription { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterIsActive")] public bool? FilterIsActive { get; init; }

    internal static readonly System.Collections.Generic.HashSet<string> QueryNames = new(System.StringComparer.Ordinal)
    { "page", "pageSize", "sortBy", "descending", "filterName", "filterDescription", "filterIsActive" };

    internal Full.NET.Abstractions.Results.Error? Validate() =>
        SortBy is not null && SortBy is not ("id" or "displayName" or "description" or "isActive")
            || FilterName?.Length > 200
            || FilterDescription?.Length > 500 ? Invalid() : null;

    internal static Full.NET.Abstractions.Results.Error Invalid() => new("catalog.products.invalid_query",
        "The generated resource query is invalid.", Full.NET.Abstractions.Results.ErrorType.Validation);

    internal static bool IsWireValueValid(string name, string? value) => name switch
    {

        _ => true,
    };

    internal System.Collections.Generic.Dictionary<string, object?> Parameters(long offset, int pageSize) => new()
    {
        ["Offset"] = offset, ["PageSize"] = pageSize, ["CodegenSortBy"] = SortBy ?? "id", ["CodegenDescending"] = Descending ?? false,
            ["Query_Name"] = Pattern(FilterName),
            ["Query_Description"] = Pattern(FilterDescription),
            ["Query_IsActive"] = FilterIsActive
    };

    // LIKE 转义同时覆盖两库通配符与 SQL Server 方括号，用户输入始终是字面量。
    private static string? Pattern(string? value) => value is null ? null : "%" + value.Replace("~", "~~", StringComparison.Ordinal)
        .Replace("%", "~%", StringComparison.Ordinal).Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal) + "%";
}
