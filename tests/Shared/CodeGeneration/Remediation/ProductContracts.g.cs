#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Acme.Remediation.Generated;

public static class ProductPermissions
{
    /// <summary>允许读取实体列表与详情。</summary>
    public const string Read = "catalog.products.read";
    /// <summary>允许创建实体。</summary>
    public const string Create = "catalog.products.create";
    /// <summary>允许更新实体。</summary>
    public const string Update = "catalog.products.update";
    /// <summary>允许停用或删除实体。</summary>
    public const string Disable = "catalog.products.disable";
}

public sealed record ProductResponse(
    [property: JsonPropertyName("id"), JsonRequired] Guid Id,
    [property: JsonPropertyName("tenantId"), JsonRequired] Guid TenantId,
    [property: JsonPropertyName("name"), JsonRequired] string Name,
    [property: JsonPropertyName("number"), JsonRequired] int Number,
    [property: JsonPropertyName("price"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal Price,
    [property: JsonPropertyName("optionalCode")] string? OptionalCode,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version,
    [property: JsonPropertyName("isDeleted"), JsonRequired] bool IsDeleted,
    [property: JsonPropertyName("deletedAtUtc"), JsonConverter(typeof(Full.NET.Hosting.Serialization.NullableUtcDateTimeJsonConverter))] DateTimeOffset? DeletedAtUtc,
    [property: JsonPropertyName("deletedById")] Guid? DeletedById);

public sealed record CreateProductRequest(
    [property: JsonPropertyName("name"), JsonRequired] string Name,
    [property: JsonPropertyName("number"), JsonRequired] int Number,
    [property: JsonPropertyName("price"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal Price,
    [property: JsonPropertyName("optionalCode")] string? OptionalCode);

public sealed record UpdateProductRequest(
    [property: JsonPropertyName("name"), JsonRequired] string Name,
    [property: JsonPropertyName("number"), JsonRequired] int Number,
    [property: JsonPropertyName("price"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal Price,
    [property: JsonPropertyName("optionalCode")] string? OptionalCode,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public sealed record DeleteProductRequest(
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public static class ProductErrorCodes
{
    public const string NotFound =
        "catalog.products.not_found";

    public const string VersionConflict =
        "catalog.products.version_conflict";

    public const string UniqueConflict =
        "catalog.products.unique_conflict";
}


/// <summary>仅开放 Schema 声明的过滤和排序字段；空过滤不参与条件，Id 为稳定分页尾键。</summary>
public sealed class ProductListQuery
{
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "sortBy")] public string? SortBy { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "descending")] public bool? Descending { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterName")] public string? FilterName { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterNumber")] public int? FilterNumber { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterPrice")] public decimal? FilterPrice { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterOptionalCode")] public string? FilterOptionalCode { get; init; }

    internal static readonly System.Collections.Generic.HashSet<string> QueryNames = new(System.StringComparer.Ordinal)
    { "page", "pageSize", "sortBy", "descending", "filterName", "filterNumber", "filterPrice", "filterOptionalCode" };

    internal Full.NET.Abstractions.Results.Error? Validate() =>
        SortBy is not null && SortBy is not ("id" or "name" or "number" or "price" or "optionalCode")
            || FilterName?.Length > 64
            || FilterOptionalCode?.Length > 64 ? Invalid() : null;

    internal static Full.NET.Abstractions.Results.Error Invalid() => new("catalog.products.invalid_query",
        "The generated resource query is invalid.", Full.NET.Abstractions.Results.ErrorType.Validation);

    internal static bool IsWireValueValid(string name, string? value) => name switch
    {
        "filterPrice" => Full.NET.Hosting.Serialization.FullNetWireScalars.TryDecimal(value, out _),
        _ => true,
    };

    internal System.Collections.Generic.Dictionary<string, object?> Parameters(long offset, int pageSize) => new()
    {
        ["Offset"] = offset, ["PageSize"] = pageSize, ["CodegenSortBy"] = SortBy ?? "id", ["CodegenDescending"] = Descending ?? false,
            ["Query_Name"] = Pattern(FilterName),
            ["Query_Number"] = FilterNumber,
            ["Query_Price"] = FilterPrice,
            ["Query_OptionalCode"] = Pattern(FilterOptionalCode)
    };

    // LIKE 转义同时覆盖两库通配符与 SQL Server 方括号，用户输入始终是字面量。
    private static string? Pattern(string? value) => value is null ? null : "%" + value.Replace("~", "~~", StringComparison.Ordinal)
        .Replace("%", "~%", StringComparison.Ordinal).Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal) + "%";
}

/// <summary>外键由已授权主端赋值，不接受调用者提供其他主端标识。</summary>
public sealed record ProductDetailInput(
    [property: JsonPropertyName("description"), JsonRequired] string Description);
public sealed record ProductDetailEdit(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version,
    [property: JsonRequired] ProductDetailInput Input);
public sealed record ProductDetailRemoval(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);
public sealed record CreateProductAggregateRequest(
    [property: JsonRequired] CreateProductRequest Master,
    [property: JsonRequired] System.Collections.Generic.IReadOnlyList<ProductDetailInput> Details);
public sealed record UpdateProductAggregateRequest(
    [property: JsonRequired] UpdateProductRequest Master,
    [property: JsonRequired] System.Collections.Generic.IReadOnlyList<ProductDetailInput> Added,
    [property: JsonRequired] System.Collections.Generic.IReadOnlyList<ProductDetailEdit> Updated,
    [property: JsonRequired] System.Collections.Generic.IReadOnlyList<ProductDetailRemoval> Removed);
public sealed record ProductAggregateResponse(
    [property: JsonRequired] ProductResponse Master,
    [property: JsonRequired] System.Collections.Generic.IReadOnlyList<ProductDetailResponse> Details);
