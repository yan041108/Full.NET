using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Tenancy.Contracts;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Tenancy.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(TenantSummary))]
[JsonSerializable(typeof(TenantContextSummary[]))]
[JsonSerializable(typeof(ChangeTenantContextRequest))]
[JsonSerializable(typeof(TenantContextTokenResponse))]
internal partial class TenancyJsonSerializerContext : JsonSerializerContext;
