using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;
using Full.NET.Modules.Ai.Persistence;

namespace Full.NET.Modules.Ai.Features.ManageMcpRemoteConnections;

internal static class AiMcpRemoteConnectionMapper
{
    internal static AiMcpRemoteConnectionListItem ToListItem(AiMcpRemoteConnectionRecord row) =>
        new(
            row.Id,
            row.ConnectionKey,
            row.DisplayName,
            AiModelConfigMasking.MaskEndpoint(row.EndpointUrl),
            !string.IsNullOrWhiteSpace(row.ServiceTokenProtected),
            row.IsEnabled,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    internal static AiMcpRemoteConnectionResponse ToResponse(AiMcpRemoteConnectionRecord row) =>
        new(
            row.Id,
            row.ConnectionKey,
            row.DisplayName,
            row.EndpointUrl,
            !string.IsNullOrWhiteSpace(row.ServiceTokenProtected),
            row.OAuthScopesJson,
            row.IsEnabled,
            row.CreatedAtUtc,
            row.UpdatedAtUtc,
            row.Version);

    internal static AiMcpRemoteToolApprovalItem ToApprovalItem(AiMcpRemoteToolApprovalRecord row) =>
        new(
            row.Id,
            row.LocalToolName,
            row.RemoteToolName,
            row.ToolVersion,
            row.SideEffectKey,
            row.PermissionCode,
            row.ApprovalStatusKey,
            row.Version);
}
