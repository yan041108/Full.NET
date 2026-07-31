#nullable enable

using System;

namespace Acme.Modules.Catalog.Generated;

internal sealed record ProductRecord(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Description,
    bool IsActive,
    long Version,
    DateTimeOffset CreatedAtUtc);
