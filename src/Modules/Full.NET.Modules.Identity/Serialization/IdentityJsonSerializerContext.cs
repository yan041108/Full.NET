using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(CurrentUserResponse))]
[JsonSerializable(typeof(UpdateLocaleRequest))]
[JsonSerializable(typeof(LocalePreferenceResponse))]
[JsonSerializable(typeof(NavigationNodeResponse[]))]
[JsonSerializable(typeof(TenantContextTokenResponse))]
[JsonSerializable(typeof(GrantSuperAdministratorRequest))]
[JsonSerializable(typeof(RevokeSuperAdministratorRequest))]
[JsonSerializable(typeof(SuperAdministratorResponse[]))]
[JsonSerializable(typeof(SuperAdministratorAuditResponse[]))]
[JsonSerializable(typeof(SuperAdministratorChangeResponse))]
[JsonSerializable(typeof(CreateHostUserRequest))]
[JsonSerializable(typeof(UpdateHostUserRequest))]
[JsonSerializable(typeof(HostUserResponse))]
[JsonSerializable(typeof(PagedResult<HostUserResponse>))]
internal partial class IdentityJsonSerializerContext : JsonSerializerContext;
