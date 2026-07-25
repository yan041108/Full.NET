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
[JsonSerializable(typeof(CreateOrganizationPositionRequest))]
[JsonSerializable(typeof(UpdateOrganizationPositionRequest))]
[JsonSerializable(typeof(OrganizationPositionResponse))]
[JsonSerializable(typeof(PagedResult<OrganizationPositionResponse>))]
[JsonSerializable(typeof(CreateOrganizationUserPositionRequest))]
[JsonSerializable(typeof(UpdateOrganizationUserPositionRequest))]
[JsonSerializable(typeof(OrganizationUserPositionResponse))]
[JsonSerializable(typeof(PagedResult<OrganizationUserPositionResponse>))]
internal partial class OrganizationJsonSerializerContext : JsonSerializerContext;
