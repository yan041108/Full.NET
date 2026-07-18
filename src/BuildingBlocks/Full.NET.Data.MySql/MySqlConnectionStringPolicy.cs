using Full.NET.Data.Abstractions;
using MySqlConnector;

namespace Full.NET.Data.MySql;

/// <summary>
/// 集中生成符合 Full.NET UUID 物理存储约束的 MySQL 连接字符串。
/// </summary>
public static class MySqlConnectionStringPolicy
{
    /// <summary>
    /// 创建采用指定 UUID 存储模式的 MySQL 连接字符串。
    /// </summary>
    /// <remarks>
    /// 调用方不得自行覆盖 GuidFormat；仅 Migrator 可将
    /// <paramref name="allowUserVariables"/> 设为 <see langword="true"/>。
    /// </remarks>
    /// <param name="connectionString">待规范化的原始 MySQL 连接字符串。</param>
    /// <param name="mode">当前部署阶段使用的 UUID 物理存储模式。</param>
    /// <param name="allowUserVariables">是否允许迁移脚本使用 MySQL 用户变量。</param>
    /// <returns>不包含冲突 UUID 映射且权限最小化的连接字符串。</returns>
    /// <exception cref="ArgumentException">连接字符串无效或显式 UUID 驱动选项与策略冲突。</exception>
    /// <exception cref="ArgumentOutOfRangeException">存储模式不是受支持的封闭枚举值。</exception>
    public static string Create(
        string connectionString,
        MySqlGuidStorageMode mode,
        bool allowUserVariables)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(mode),
                "不支持指定的 MySQL UUID 存储模式。");
        }

        MySqlConnectionStringBuilder builder;
        try
        {
            builder = new MySqlConnectionStringBuilder(connectionString);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            // 原始异常可能包含连接属性值，因此只返回不携带内部异常的固定错误。
            throw new ArgumentException(
                "MySQL 连接字符串无效。",
                nameof(connectionString));
        }

        if (builder.ContainsKey(nameof(MySqlConnectionStringBuilder.OldGuids)))
        {
            throw new ArgumentException(
                "连接字符串中的 Old Guids 与 Full.NET MySQL UUID 存储策略冲突。",
                nameof(connectionString));
        }

        ValidateExplicitGuidFormat(builder, mode);
        if (mode == MySqlGuidStorageMode.Binary16)
        {
            builder.GuidFormat = MySqlGuidFormat.Binary16;
        }
        else
        {
            builder.Remove(nameof(MySqlConnectionStringBuilder.GuidFormat));
        }

        builder.AllowUserVariables = allowUserVariables;
        return builder.ConnectionString;
    }

    private static void ValidateExplicitGuidFormat(
        MySqlConnectionStringBuilder builder,
        MySqlGuidStorageMode mode)
    {
        if (!builder.ContainsKey(nameof(MySqlConnectionStringBuilder.GuidFormat)))
        {
            return;
        }

        var guidFormat = builder.GuidFormat;
        var isAlwaysRejected = guidFormat == MySqlGuidFormat.Char36;
        var isSelectedMode = mode switch
        {
            MySqlGuidStorageMode.LegacyChar36 => guidFormat == MySqlGuidFormat.Default,
            MySqlGuidStorageMode.Binary16 => guidFormat == MySqlGuidFormat.Binary16,
            _ => false,
        };
        if (isAlwaysRejected || !isSelectedMode)
        {
            throw new ArgumentException(
                "连接字符串中的 GuidFormat 与 Full.NET MySQL UUID 存储策略冲突。",
                "connectionString");
        }
    }
}
