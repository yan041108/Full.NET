namespace Full.NET.Modules.Ai.Domain;

/// <summary>文档元数据边界；不接受正文、文件标识或索引就绪状态。</summary>
internal static class AiKnowledgeDocuments
{
    internal static string? Validate(string? title, string? description) =>
        string.IsNullOrWhiteSpace(title) || title.Trim().Length > 200 || description?.Length > 2000
            ? "Title is required and limited to 200 characters; description is limited to 2000 characters." : null;
}
