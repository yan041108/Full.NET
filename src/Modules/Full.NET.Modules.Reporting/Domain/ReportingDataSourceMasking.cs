namespace Full.NET.Modules.Reporting.Domain;

/// <summary>报表数据源列表脱敏辅助；详情仍返回结构化字段，但列表永不暴露完整连接信息。</summary>
internal static class ReportingDataSourceMasking
{
    /// <summary>脱敏服务器端点显示文本。</summary>
    /// <param name="serverHost">服务器主机。</param>
    /// <param name="port">端口。</param>
    /// <returns>脱敏后的 host:port。</returns>
    public static string MaskServerEndpoint(string serverHost, int port) =>
        $"{MaskToken(serverHost)}:{port}";

    /// <summary>脱敏数据库名或用户名。</summary>
    /// <param name="value">原始值。</param>
    /// <returns>脱敏文本。</returns>
    public static string MaskIdentifier(string value) =>
        MaskToken(value);

    private static string MaskToken(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length <= 2)
        {
            return "***";
        }

        if (normalized.Length <= 4)
        {
            return normalized[0] + "***";
        }

        return normalized[..2] + "***" + normalized[^1];
    }
}
