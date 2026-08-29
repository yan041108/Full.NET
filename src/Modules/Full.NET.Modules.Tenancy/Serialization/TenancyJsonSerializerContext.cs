using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Abstractions.Tenancy;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(TenantSummary))]
[JsonSerializable(typeof(PagedResult<TenantSummary>))]
[JsonSerializable(typeof(TenantContextSummary[]))]
[JsonSerializable(typeof(ChangeTenantContextRequest))]
[JsonSerializable(typeof(TenantContextTokenResponse))]
[JsonSerializable(typeof(ProvisionTenantRequest))]
[JsonSerializable(typeof(UpdateHostTenantRequest))]
[JsonSerializable(typeof(TenantCachePayload))]
[JsonSerializable(typeof(TenantResolutionCacheEntry))]
[JsonSerializable(typeof(AssignHostTenantPackageRequest))]
[JsonSerializable(typeof(TenantPackageSummary))]
[JsonSerializable(typeof(PagedResult<TenantPackageSummary>))]
[JsonSerializable(typeof(CreateHostTenantPackageRequest))]
[JsonSerializable(typeof(UpdateHostTenantPackageRequest))]
internal partial class TenancyJsonSerializerContext : JsonSerializerContext;
