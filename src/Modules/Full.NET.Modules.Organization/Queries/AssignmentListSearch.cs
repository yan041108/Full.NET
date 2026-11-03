namespace Full.NET.Modules.Organization.Queries;

/// <summary>机构及职位隶属列表的字面量搜索参数；通配符只由服务端添加。</summary>
internal static class AssignmentListSearch
{
    internal const int MaximumLength = 100;

    internal static bool IsValid(string? value) => value?.Trim().Length is not > MaximumLength;

    internal static string? Pattern(string? value) => string.IsNullOrWhiteSpace(value)
        ? null
        : $"%{value.Trim().Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal)
            .Replace("[", "![", StringComparison.Ordinal)}%";
}
