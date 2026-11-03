#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Acme.Remediation.Generated;

public static class ProductItemPermissions
{
    /// <summary>允许读取实体列表与详情。</summary>
    public const string Read = "catalog.product_items.read";
    /// <summary>允许创建实体。</summary>
    public const string Create = "catalog.product_items.create";
    /// <summary>允许更新实体。</summary>
    public const string Update = "catalog.product_items.update";
    /// <summary>允许停用或删除实体。</summary>
    public const string Disable = "catalog.product_items.disable";
}

public sealed record ProductItemResponse(
    [property: JsonPropertyName("id"), JsonRequired] Guid Id,
    [property: JsonPropertyName("tenantId"), JsonRequired] Guid TenantId,
    [property: JsonPropertyName("productId"), JsonRequired] Guid ProductId,
    [property: JsonPropertyName("description"), JsonRequired] string Description,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version,
    [property: JsonPropertyName("isDeleted"), JsonRequired] bool IsDeleted,
    [property: JsonPropertyName("deletedAtUtc"), JsonConverter(typeof(Full.NET.Hosting.Serialization.NullableUtcDateTimeJsonConverter))] DateTimeOffset? DeletedAtUtc,
    [property: JsonPropertyName("deletedById")] Guid? DeletedById);

public sealed record CreateProductItemRequest(
    [property: JsonPropertyName("productId"), JsonRequired] Guid ProductId,
    [property: JsonPropertyName("description"), JsonRequired] string Description);

public sealed record UpdateProductItemRequest(
    [property: JsonPropertyName("productId"), JsonRequired] Guid ProductId,
    [property: JsonPropertyName("description"), JsonRequired] string Description,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public sealed record DeleteProductItemRequest(
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public static class ProductItemErrorCodes
{
    public const string NotFound =
        "catalog.product_items.not_found";

    public const string VersionConflict =
        "catalog.product_items.version_conflict";
}


/// <summary>仅开放 Schema 声明的过滤和排序字段；空过滤不参与条件，Id 为稳定分页尾键。</summary>
public sealed class ProductItemListQuery
{
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "sortBy")] public string? SortBy { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "descending")] public bool? Descending { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterProductId")] public Guid? FilterProductId { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterDescription")] public string? FilterDescription { get; init; }

    internal static readonly System.Collections.Generic.HashSet<string> QueryNames = new(System.StringComparer.Ordinal)
    { "page", "pageSize", "sortBy", "descending", "filterProductId", "filterDescription" };

    internal Full.NET.Abstractions.Results.Error? Validate() =>
        SortBy is not null && SortBy is not ("id" or "productId" or "description")
            || FilterDescription?.Length > 64
            || FilterProductId == Guid.Empty ? Invalid() : null;

    internal static Full.NET.Abstractions.Results.Error Invalid() => new("catalog.product_items.invalid_query",
        "The generated resource query is invalid.", Full.NET.Abstractions.Results.ErrorType.Validation);

    internal static bool IsWireValueValid(string name, string? value) => name switch
    {

        _ => true,
    };

    internal System.Collections.Generic.Dictionary<string, object?> Parameters(long offset, int pageSize) => new()
    {
        ["Offset"] = offset, ["PageSize"] = pageSize, ["CodegenSortBy"] = SortBy ?? "id", ["CodegenDescending"] = Descending ?? false,
            ["Query_ProductId"] = FilterProductId,
            ["Query_Description"] = Pattern(FilterDescription)
    };

    // LIKE 转义同时覆盖两库通配符与 SQL Server 方括号，用户输入始终是字面量。
    private static string? Pattern(string? value) => value is null ? null : "%" + value.Replace("~", "~~", StringComparison.Ordinal)
        .Replace("%", "~%", StringComparison.Ordinal).Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal) + "%";
}
