using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(CurrentUserResponse))]
[JsonSerializable(typeof(NavigationNodeResponse[]))]
[JsonSerializable(typeof(TenantContextTokenResponse))]
internal partial class IdentityJsonSerializerContext : JsonSerializerContext;
