namespace Full.NET.Modules.Ai.Domain;

/// <summary>Embedding 测试输入边界；完整向量不回显给客户端。</summary>
internal static class AiEmbeddingContentPolicy
{
    /// <summary>单条输入最大字符数。</summary>
    internal const int MaxInputLength = 8000;

    /// <summary>批量输入最大条数。</summary>
    internal const int MaxBatchSize = 16;

    /// <summary>批量输入总字符上限。</summary>
    internal const int MaxTotalCharacters = 32000;

    /// <summary>校验测试输入。</summary>
    public static string? ValidateInputs(IReadOnlyList<string> inputs)
    {
        if (inputs.Count == 0)
            return "At least one embedding input is required.";

        if (inputs.Count > MaxBatchSize)
            return $"Embedding batch must not exceed {MaxBatchSize} inputs.";

        long total = 0;
        foreach (var input in inputs)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "Embedding input cannot be empty.";
            if (input.Length > MaxInputLength)
                return $"Each embedding input must not exceed {MaxInputLength} characters.";
            total = checked(total + input.Length);
            if (total > MaxTotalCharacters)
                return $"Embedding inputs must not exceed {MaxTotalCharacters} total characters.";
        }

        return null;
    }
}
