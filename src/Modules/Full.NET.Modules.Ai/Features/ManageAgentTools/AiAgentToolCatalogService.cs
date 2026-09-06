using Full.NET.Abstractions.Results;
using Full.NET.Modules.Ai.Contracts;
using Full.NET.Modules.Ai.Domain;

namespace Full.NET.Modules.Ai.Features.ManageAgentTools;

/// <summary>静态 Agent Tool 目录查询。</summary>
internal static class AiAgentToolCatalogService
{
    /// <summary>列出全部已登记工具。</summary>
    public static Result<IReadOnlyList<AiAgentToolCatalogItem>> List() =>
        Result<IReadOnlyList<AiAgentToolCatalogItem>>.Success(AiAgentToolCatalog.List());

    /// <summary>按工具名读取目录项。</summary>
    /// <param name="toolName">工具名。</param>
    public static Result<AiAgentToolCatalogItem> GetByName(string toolName)
    {
        var item = AiAgentToolCatalog.Find(toolName);
        return item is null
            ? Result<AiAgentToolCatalogItem>.Failure(new Error(
                AiErrorCodes.AgentToolNotFound,
                "The AI agent tool was not found.",
                ErrorType.NotFound))
            : Result<AiAgentToolCatalogItem>.Success(item);
    }
}
