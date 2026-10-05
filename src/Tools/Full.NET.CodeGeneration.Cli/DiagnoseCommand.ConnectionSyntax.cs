using System.Text.Json;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    private static bool HasValidConnectionSyntax(
        JsonElement root, JsonDocument? profileSettings, string workspacePath, string profile,
        string connectionString)
    {
        _ = TryReadDatabaseValue(root, profileSettings, workspacePath, profile, "Database:Provider", out var value);
        var provider = DiagnosticDatabaseProvider.SqlServer;
        // 无效 Provider 由原有独立诊断负责，不能再把它误报为连接串错误。
        if (value is not null && (!Enum.TryParse(value, true, out provider) || !Enum.IsDefined(provider)))
            return true;

        try
        {
            if (provider == DiagnosticDatabaseProvider.SqlServer)
            {
                // 与真实工厂一样只构造未打开连接，保留 SQL Server 对重复键最终值的解析语义。
                using var connection = new SqlConnection(connectionString);
            }
            else
            {
                // MySQL 工厂先经过此驱动解析器；不打开连接，也不在此扩展 UUID 或连接池准入。
                _ = new MySqlConnectionStringBuilder(connectionString);
            }
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
        {
            // 驱动异常可能包含键名或凭据，调用方仅输出固定机器码与脱敏提示。
            return false;
        }
    }
}
