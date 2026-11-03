#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Acme.Remediation.Generated;

public static class ProductTagPermissions
{
    /// <summary>允许读取实体列表与详情。</summary>
    public const string Read = "catalog.product_tags.read";
    /// <summary>允许创建实体。</summary>
    public const string Create = "catalog.product_tags.create";
    /// <summary>允许更新实体。</summary>
    public const string Update = "catalog.product_tags.update";
    /// <summary>允许停用或删除实体。</summary>
    public const string Disable = "catalog.product_tags.disable";
}

public sealed record ProductTagResponse(
    [property: JsonPropertyName("id"), JsonRequired] Guid Id,
    [property: JsonPropertyName("tenantId"), JsonRequired] Guid TenantId,
    [property: JsonPropertyName("productReferenceId"), JsonRequired] Guid ProductReferenceId,
    [property: JsonPropertyName("tagId"), JsonRequired] Guid TagId,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version,
    [property: JsonPropertyName("isDeleted"), JsonRequired] bool IsDeleted,
    [property: JsonPropertyName("deletedAtUtc"), JsonConverter(typeof(Full.NET.Hosting.Serialization.NullableUtcDateTimeJsonConverter))] DateTimeOffset? DeletedAtUtc,
    [property: JsonPropertyName("deletedById")] Guid? DeletedById);

public sealed record CreateProductTagRequest(
    [property: JsonPropertyName("productReferenceId"), JsonRequired] Guid ProductReferenceId,
    [property: JsonPropertyName("tagId"), JsonRequired] Guid TagId);

public sealed record UpdateProductTagRequest(
    [property: JsonPropertyName("productReferenceId"), JsonRequired] Guid ProductReferenceId,
    [property: JsonPropertyName("tagId"), JsonRequired] Guid TagId,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public sealed record DeleteProductTagRequest(
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public static class ProductTagErrorCodes
{
    public const string NotFound =
        "catalog.product_tags.not_found";

    public const string VersionConflict =
        "catalog.product_tags.version_conflict";

    public const string UniqueConflict =
        "catalog.product_tags.unique_conflict";

    public const string PrincipalNotFound =
        "catalog.product_tags.principal_not_found";
}


/// <summary>仅开放 Schema 声明的过滤和排序字段；空过滤不参与条件，Id 为稳定分页尾键。</summary>
public sealed class ProductTagListQuery
{
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "sortBy")] public string? SortBy { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "descending")] public bool? Descending { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterProductReferenceId")] public Guid? FilterProductReferenceId { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterTagId")] public Guid? FilterTagId { get; init; }

    internal static readonly System.Collections.Generic.HashSet<string> QueryNames = new(System.StringComparer.Ordinal)
    { "page", "pageSize", "sortBy", "descending", "filterProductReferenceId", "filterTagId" };

    internal Full.NET.Abstractions.Results.Error? Validate() =>
        SortBy is not null && SortBy is not ("id" or "productReferenceId" or "tagId")
            || FilterProductReferenceId == Guid.Empty
            || FilterTagId == Guid.Empty ? Invalid() : null;

    internal static Full.NET.Abstractions.Results.Error Invalid() => new("catalog.product_tags.invalid_query",
        "The generated resource query is invalid.", Full.NET.Abstractions.Results.ErrorType.Validation);

    internal static bool IsWireValueValid(string name, string? value) => name switch
    {

        _ => true,
    };

    internal System.Collections.Generic.Dictionary<string, object?> Parameters(long offset, int pageSize) => new()
    {
        ["Offset"] = offset, ["PageSize"] = pageSize, ["CodegenSortBy"] = SortBy ?? "id", ["CodegenDescending"] = Descending ?? false,
            ["Query_ProductId"] = FilterProductReferenceId,
            ["Query_TagId"] = FilterTagId
    };

    // LIKE 转义同时覆盖两库通配符与 SQL Server 方括号，用户输入始终是字面量。
    private static string? Pattern(string? value) => value is null ? null : "%" + value.Replace("~", "~~", StringComparison.Ordinal)
        .Replace("%", "~%", StringComparison.Ordinal).Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal) + "%";
}


/// <summary>关联读取权限下的最小候选投影，不暴露主端完整 DTO。</summary>
public sealed record ProductTagRelationOption([property: JsonRequired] Guid Value, [property: JsonRequired] string Label);
