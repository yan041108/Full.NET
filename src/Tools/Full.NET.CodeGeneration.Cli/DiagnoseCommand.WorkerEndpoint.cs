namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    private static bool MatchesWorkerHealthEndpoint(string? endpoint, int expectedPort)
    {
        if (string.IsNullOrEmpty(endpoint)) return false;
        var schemeEnd = endpoint.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return false;
        var scheme = endpoint[..schemeEnd];
        if (!scheme.Equals("http", StringComparison.OrdinalIgnoreCase)
            && !scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) return false;

        var authorityStart = schemeEnd + 3;
        var pathStart = endpoint.IndexOf('/', authorityStart);
        // Kestrel 允许末尾根斜线，但不会把 /./ 等路径规范化后作为监听地址接受。
        if (pathStart >= 0 && !endpoint.AsSpan(pathStart).SequenceEqual("/")) return false;
        // URI 将查询或片段从端口分离，监听解析器却可能退回默认端口，不能据此认证档案端口。
        if (endpoint.IndexOfAny(['?', '#'], authorityStart) >= 0) return false;

        var authority = endpoint[authorityStart..(pathStart < 0 ? endpoint.Length : pathStart)];
        var address = endpoint;
        if (authority is "+" or "*" || authority.StartsWith("+:", StringComparison.Ordinal)
            || authority.StartsWith("*:", StringComparison.Ordinal))
        {
            // 通配监听是 Kestrel 的合法输入；仅替换主机以复用 URI 的端口校验，不增加 CLI 的 Web 运行依赖。
            address = endpoint[..authorityStart] + "localhost" + endpoint[(authorityStart + 1)..];
        }
        return Uri.TryCreate(address, UriKind.Absolute, out var parsed) && parsed.Port == expectedPort;
    }
}
