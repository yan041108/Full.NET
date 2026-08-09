using System.Text.RegularExpressions;
using DbUp.Engine;

namespace Full.NET.Migrations.DbUp;

/// <summary>
/// 保持已记账 094 脚本字节不变，同时在 MySQL 8 执行前移除其不支持的
/// ADD CONSTRAINT IF NOT EXISTS 语句；紧随其后的 095 负责幂等补齐约束。
/// </summary>
internal sealed partial class MySqlPublishedMigrationCompatibilityPreprocessor
    : IScriptPreprocessor
{
    public string Process(string contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        return UnsupportedConstraintSyntax().Replace(
            contents,
            "-- 094 compatibility: constraints converge in migration 095.\n");
    }

    [GeneratedRegex(
        @"(?ms)^\s*ALTER\s+TABLE\s+fn_messaging_stream_ownership\s+ADD\s+CONSTRAINT\s+IF\s+NOT\s+EXISTS\s+CK_fn_messaging_stream_ownership_(?:SchemaVersion|CurrentOwner|PreviousOwner)\s+CHECK\s*\([^;]+;\s*")]
    private static partial Regex UnsupportedConstraintSyntax();
}
