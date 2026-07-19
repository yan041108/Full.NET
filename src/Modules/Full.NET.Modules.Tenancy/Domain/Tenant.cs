using Full.NET.Localization;

namespace Full.NET.Modules.Tenancy.Domain;

internal sealed record Tenant(
    Guid Id,
    string Identifier,
    string Name,
    string Domain,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    int Version,
    string DefaultLocale = LocaleCatalog.DefaultLocale);
