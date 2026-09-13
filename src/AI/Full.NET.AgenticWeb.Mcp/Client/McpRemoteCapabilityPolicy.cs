using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Full.NET.Agents.Mcp;

namespace Full.NET.AgenticWeb.Mcp.Client;

/// <summary>远端 MCP 能力治理；远端只读声明不足以获得豁免。</summary>
public static class McpRemoteCapabilityPolicy
{
    /// <summary>已批准且未漂移。</summary>
    public const string ApprovedStatusKey = "approved";

    /// <summary>Schema 或版本漂移后自动禁用。</summary>
    public const string DriftDisabledStatusKey = "disabled_drift";

    /// <summary>构建本地注册名；禁止模型任意拼接。</summary>
    public static string BuildLocalToolName(string connectionKey, string remoteToolName) =>
        $"remote.{connectionKey}.{remoteToolName}";

    /// <summary>计算输入 Schema 的稳定哈希。</summary>
    public static string ComputeSchemaHash(string inputSchemaJson)
    {
        using var document = JsonDocument.Parse(inputSchemaJson);
        var normalized = JsonSerializer.Serialize(
            document.RootElement,
            McpJsonSerializerContext.Default.JsonElement);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    /// <summary>是否允许将远端工具固化为本地注册项。</summary>
    public static bool CanApproveForExposure(string sideEffectKey, string inputSchemaJson, out string? rejection)
    {
        rejection = null;
        if (sideEffectKey is not ("none" or "read"))
        {
            rejection = "ai.mcp.remote_write_forbidden";
            return false;
        }

        if (string.IsNullOrWhiteSpace(inputSchemaJson))
        {
            rejection = "ai.mcp.remote_schema_missing";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(inputSchemaJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                rejection = "ai.mcp.remote_schema_invalid";
                return false;
            }
        }
        catch (JsonException)
        {
            rejection = "ai.mcp.remote_schema_invalid";
            return false;
        }

        return true;
    }

    /// <summary>执行前校验批准记录仍有效。</summary>
    public static bool CanExecute(McpRemoteApprovedTool tool, string? liveSchemaHash, out string? rejection)
    {
        rejection = null;
        if (!string.Equals(tool.ApprovalStatusKey, ApprovedStatusKey, StringComparison.Ordinal))
        {
            rejection = "ai.mcp.remote_not_approved";
            return false;
        }

        if (tool.SideEffectKey is not ("none" or "read"))
        {
            rejection = "ai.mcp.remote_write_forbidden";
            return false;
        }

        if (liveSchemaHash is not null
            && !string.Equals(liveSchemaHash, tool.InputSchemaHash, StringComparison.Ordinal))
        {
            rejection = "ai.mcp.remote_schema_drift";
            return false;
        }

        return true;
    }
}
