namespace Full.NET.Data.CodeGeneration.Generation;

/// <summary>校验写入静态 TypeScript import 的受控模块标识，防止路径文本变成源码。</summary>
internal static class TypeScriptModuleSpecifier
{
    internal static string Validate(string value)
    {
        // 模块可以是相对文件或包导出；不允许 URL、转义、空段或带副作用的源码片段。
        if (string.IsNullOrEmpty(value) || value.Length > 240 || value.EndsWith("/", StringComparison.Ordinal)
            || value.StartsWith("/", StringComparison.Ordinal) || value.Contains("//", StringComparison.Ordinal)
            || value is "." or ".." || value.StartsWith('#')
            || value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '_' or '-' or '.' or '/' or '@'))
            || value.Split('/').Any(segment => segment.Length == 0))
            throw new ArgumentException("客户端模块必须是非空静态 TypeScript 相对路径或包标识。", nameof(value));
        return value;
    }
}
