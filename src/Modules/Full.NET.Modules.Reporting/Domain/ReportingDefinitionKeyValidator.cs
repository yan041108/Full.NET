using System.Text.RegularExpressions;
using Full.NET.Modules.Reporting.Contracts;

namespace Full.NET.Modules.Reporting.Domain;

/// <summary>报表定义键格式校验。</summary>
internal static partial class ReportingDefinitionKeyValidator
{
    [GeneratedRegex("^[a-z][a-z0-9._-]{1,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex DefinitionKeyPattern();

    /// <summary>校验定义键是否符合稳定机器码格式。</summary>
    /// <param name="definitionKey">定义键。</param>
    /// <returns>是否有效。</returns>
    public static bool IsValid(string? definitionKey) =>
        !string.IsNullOrWhiteSpace(definitionKey)
        && DefinitionKeyPattern().IsMatch(definitionKey.Trim());
}
