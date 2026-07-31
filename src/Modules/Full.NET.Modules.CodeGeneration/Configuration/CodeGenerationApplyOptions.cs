namespace Full.NET.Modules.CodeGeneration.Configuration;

/// <summary>
/// 配置 Host 代码生成 Apply 使用的服务器本地工作区；默认禁用以避免宿主意外写盘。
/// </summary>
internal sealed class CodeGenerationApplyOptions
{
    public const string SectionName = "CodeGeneration:Apply";

    public bool Enabled { get; set; }

    public string WorkspaceRoot { get; set; } = string.Empty;
}
