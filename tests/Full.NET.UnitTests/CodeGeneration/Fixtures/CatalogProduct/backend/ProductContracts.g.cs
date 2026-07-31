#nullable enable

using System;
using System.Text.Json.Serialization;

namespace Acme.Modules.Catalog.Generated;

public static class ProductPermissions
{
    public const string Read = "catalog.products.read";
    public const string Write = "catalog.products.write";
}

public sealed record ProductResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("tenantId")] Guid TenantId,
    [property: JsonPropertyName("displayName")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("isActive")] bool IsActive,
    [property: JsonPropertyName("version"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long Version,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc);

public sealed record CreateProductRequest(
    [property: JsonPropertyName("displayName")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("isActive")] bool IsActive);

public sealed record UpdateProductRequest(
    [property: JsonPropertyName("displayName")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("isActive")] bool IsActive,
    [property: JsonPropertyName("version"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long Version);

public sealed record DisableProductRequest(
    [property: JsonPropertyName("version"), JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)] long Version);

public static class ProductErrorCodes
{
    public const string NotFound =
        "catalog.products.not_found";

    public const string VersionConflict =
        "catalog.products.version_conflict";
}
