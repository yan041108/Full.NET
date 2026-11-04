using System.Text.Json;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Microsoft.Data.SqlClient;
using MySqlConnector;

namespace Full.NET.CodeGeneration.Cli;

internal static partial class DiagnoseCommand
{
    private static bool HasValidConnectionConfiguration(
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
                _ = TryReadDatabaseValue(root, profileSettings, workspacePath, profile,
                    "Database:MySqlGuidStorageMode", out var storageValue);
                var mode = MySqlGuidStorageMode.LegacyChar36;
                if (storageValue is not null && (!Enum.TryParse(storageValue, true, out mode) || !Enum.IsDefined(mode)))
                {
                    // 无效存储模式由独立选项诊断负责，仍保留连接串语法检查。
                    _ = new MySqlConnectionStringBuilder(connectionString);
                }
                else
                {
                    // 复用真实工厂的 UUID 策略；只验证，不保存规范化结果，也不打开连接。
                    _ = MySqlConnectionStringPolicy.Create(connectionString, mode, allowUserVariables: false);
                }
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
