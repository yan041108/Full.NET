using System.Security.Cryptography;
using System.Text;
using DbUp.Engine;

namespace Full.NET.Migrations.DbUp;

/// <summary>在执行边界兼容已记账脚本的批次编译缺陷，不改写历史资源。</summary>
internal sealed class SqlServerPublishedMigrationCompatibilityPreprocessor : IScriptPreprocessor
{
    private const string Document093Digest = "AED629A90060DE79B75F1C5EDA9447B423AB84287BA50F998747424C0D6B8A64";

    /// <summary>仅在已冻结全文中延迟编译两个回填语句；数据、幂等探测和记账名称保持不变。</summary>
    public string Process(string contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        var normalized = contents.ReplaceLineEndings("\n");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        if (digest != Document093Digest) return contents;

        // SQL Server 会在 ALTER ADD 执行前绑定同批次 UPDATE 列；固定正文延迟编译后才能无损恢复旧表。
        return normalized
            .Replace("UPDATE dbo.fn_document_share\n            SET PasswordHash = Password\n            WHERE PasswordHash IS NULL;",
                "EXEC sys.sp_executesql N'UPDATE dbo.fn_document_share SET PasswordHash = Password WHERE PasswordHash IS NULL;';", StringComparison.Ordinal)
            .Replace("UPDATE dbo.fn_document_share\n            SET PasswordHash = COALESCE(PasswordHash, Password)\n            WHERE PasswordHash IS NULL;",
                "EXEC sys.sp_executesql N'UPDATE dbo.fn_document_share SET PasswordHash = COALESCE(PasswordHash, Password) WHERE PasswordHash IS NULL;';", StringComparison.Ordinal);
    }
}
