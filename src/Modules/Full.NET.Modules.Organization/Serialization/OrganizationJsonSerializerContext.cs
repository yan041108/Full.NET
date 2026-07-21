using System.Text.Json;
using System.Text.Json.Serialization;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Organization.Contracts;

namespace Full.NET.Modules.Organization.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(CreateOrganizationUnitRequest))]
[JsonSerializable(typeof(UpdateOrganizationUnitRequest))]
[JsonSerializable(typeof(OrganizationUnitResponse))]
[JsonSerializable(typeof(PagedResult<OrganizationUnitResponse>))]
[JsonSerializable(typeof(CreateOrganizationUserUnitRequest))]
[JsonSerializable(typeof(UpdateOrganizationUserUnitRequest))]
[JsonSerializable(typeof(OrganizationUserUnitResponse))]
[JsonSerializable(typeof(PagedResult<OrganizationUserUnitResponse>))]
internal partial class OrganizationJsonSerializerContext : JsonSerializerContext;
