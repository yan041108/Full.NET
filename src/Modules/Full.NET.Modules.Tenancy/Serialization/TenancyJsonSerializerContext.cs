using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Tenancy.Contracts;

namespace Full.NET.Modules.Tenancy.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(TenantSummary))]
internal partial class TenancyJsonSerializerContext : JsonSerializerContext;
