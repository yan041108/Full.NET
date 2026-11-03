#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Full.NET.Modules.EnterpriseRequest.Generated;

public static class EnterpriseRequestDetailPermissions
{
    /// <summary>允许读取实体列表与详情。</summary>
    public const string Read = "enterprise_request.enterprise_request_lines.read";
    /// <summary>允许创建实体。</summary>
    public const string Create = "enterprise_request.enterprise_request_lines.create";
    /// <summary>允许更新实体。</summary>
    public const string Update = "enterprise_request.enterprise_request_lines.update";
    /// <summary>允许停用或删除实体。</summary>
    public const string Disable = "enterprise_request.enterprise_request_lines.disable";
}

public sealed record EnterpriseRequestDetailResponse(
    [property: JsonPropertyName("id"), JsonRequired] Guid Id,
    [property: JsonPropertyName("tenantId"), JsonRequired] Guid TenantId,
    [property: JsonPropertyName("organizationUnitId"), JsonRequired] Guid OrganizationUnitId,
    [property: JsonPropertyName("requestId"), JsonRequired] Guid RequestId,
    [property: JsonPropertyName("lineNumber"), JsonRequired] int LineNumber,
    [property: JsonPropertyName("itemDescription"), JsonRequired] string ItemDescription,
    [property: JsonPropertyName("quantity"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal Quantity,
    [property: JsonPropertyName("unitPrice"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal UnitPrice,
    [property: JsonPropertyName("lineAmount"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal LineAmount,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version,
    [property: JsonPropertyName("createdAtUtc"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.UtcDateTimeJsonConverter))] DateTimeOffset CreatedAtUtc,
    [property: JsonPropertyName("createdById"), JsonRequired] Guid CreatedById,
    [property: JsonPropertyName("updatedAtUtc"), JsonConverter(typeof(Full.NET.Hosting.Serialization.NullableUtcDateTimeJsonConverter))] DateTimeOffset? UpdatedAtUtc,
    [property: JsonPropertyName("updatedById")] Guid? UpdatedById,
    [property: JsonPropertyName("isDeleted"), JsonRequired] bool IsDeleted,
    [property: JsonPropertyName("deletedAtUtc"), JsonConverter(typeof(Full.NET.Hosting.Serialization.NullableUtcDateTimeJsonConverter))] DateTimeOffset? DeletedAtUtc,
    [property: JsonPropertyName("deletedById")] Guid? DeletedById);

public sealed record CreateEnterpriseRequestDetailRequest(
    [property: JsonPropertyName("requestId"), JsonRequired] Guid RequestId,
    [property: JsonPropertyName("lineNumber"), JsonRequired] int LineNumber,
    [property: JsonPropertyName("itemDescription"), JsonRequired] string ItemDescription,
    [property: JsonPropertyName("quantity"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal Quantity,
    [property: JsonPropertyName("unitPrice"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal UnitPrice,
    [property: JsonPropertyName("lineAmount"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal LineAmount);

public sealed record UpdateEnterpriseRequestDetailRequest(
    [property: JsonPropertyName("requestId"), JsonRequired] Guid RequestId,
    [property: JsonPropertyName("lineNumber"), JsonRequired] int LineNumber,
    [property: JsonPropertyName("itemDescription"), JsonRequired] string ItemDescription,
    [property: JsonPropertyName("quantity"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal Quantity,
    [property: JsonPropertyName("unitPrice"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal UnitPrice,
    [property: JsonPropertyName("lineAmount"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringDecimalJsonConverter))] decimal LineAmount,
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public sealed record DeleteEnterpriseRequestDetailRequest(
    [property: JsonPropertyName("version"), JsonRequired, JsonConverter(typeof(Full.NET.Hosting.Serialization.StringInt64JsonConverter))] long Version);

public static class EnterpriseRequestDetailErrorCodes
{
    public const string NotFound =
        "enterprise_request.enterprise_request_lines.not_found";

    public const string VersionConflict =
        "enterprise_request.enterprise_request_lines.version_conflict";
}


/// <summary>仅开放 Schema 声明的过滤和排序字段；空过滤不参与条件，Id 为稳定分页尾键。</summary>
public sealed class EnterpriseRequestDetailListQuery
{
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "sortBy")] public string? SortBy { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "descending")] public bool? Descending { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterRequestId")] public Guid? FilterRequestId { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterLineNumber")] public int? FilterLineNumber { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterItemDescription")] public string? FilterItemDescription { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterQuantity")] public decimal? FilterQuantity { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterUnitPrice")] public decimal? FilterUnitPrice { get; init; }
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = "filterLineAmount")] public decimal? FilterLineAmount { get; init; }

    internal static readonly System.Collections.Generic.HashSet<string> QueryNames = new(System.StringComparer.Ordinal)
    { "page", "pageSize", "sortBy", "descending", "filterRequestId", "filterLineNumber", "filterItemDescription", "filterQuantity", "filterUnitPrice", "filterLineAmount" };

    internal Full.NET.Abstractions.Results.Error? Validate() =>
        SortBy is not null && SortBy is not ("id" or "requestId" or "lineNumber" or "itemDescription" or "quantity" or "unitPrice" or "lineAmount")
            || FilterItemDescription?.Length > 200
            || FilterRequestId == Guid.Empty ? Invalid() : null;

    internal static Full.NET.Abstractions.Results.Error Invalid() => new("enterprise_request.enterprise_request_lines.invalid_query",
        "The generated resource query is invalid.", Full.NET.Abstractions.Results.ErrorType.Validation);

    internal static bool IsWireValueValid(string name, string? value) => name switch
    {
        "filterQuantity" => Full.NET.Hosting.Serialization.FullNetWireScalars.TryDecimal(value, out _),
        "filterUnitPrice" => Full.NET.Hosting.Serialization.FullNetWireScalars.TryDecimal(value, out _),
        "filterLineAmount" => Full.NET.Hosting.Serialization.FullNetWireScalars.TryDecimal(value, out _),
        _ => true,
    };

    internal System.Collections.Generic.Dictionary<string, object?> Parameters(long offset, int pageSize) => new()
    {
        ["Offset"] = offset, ["PageSize"] = pageSize, ["CodegenSortBy"] = SortBy ?? "id", ["CodegenDescending"] = Descending ?? false,
            ["Query_RequestId"] = FilterRequestId,
            ["Query_LineNumber"] = FilterLineNumber,
            ["Query_ItemDescription"] = Pattern(FilterItemDescription),
            ["Query_Quantity"] = FilterQuantity,
            ["Query_UnitPrice"] = FilterUnitPrice,
            ["Query_LineAmount"] = FilterLineAmount
    };

    // LIKE 转义同时覆盖两库通配符与 SQL Server 方括号，用户输入始终是字面量。
    private static string? Pattern(string? value) => value is null ? null : "%" + value.Replace("~", "~~", StringComparison.Ordinal)
        .Replace("%", "~%", StringComparison.Ordinal).Replace("_", "~_", StringComparison.Ordinal).Replace("[", "~[", StringComparison.Ordinal) + "%";
}
