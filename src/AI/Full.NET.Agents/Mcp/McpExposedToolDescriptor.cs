namespace Full.NET.Agents.Mcp;

/// <summary>MCP 对外宣告的工具元数据。</summary>
/// <param name="ToolName">稳定工具名。</param>
/// <param name="DisplayName">展示名称。</param>
/// <param name="Description">描述。</param>
/// <param name="InputSchemaJson">JSON Schema 字符串。</param>
/// <param name="ToolVersion">工具版本。</param>
public sealed record McpExposedToolDescriptor(
    string ToolName,
    string DisplayName,
    string Description,
    string InputSchemaJson,
    int ToolVersion);
