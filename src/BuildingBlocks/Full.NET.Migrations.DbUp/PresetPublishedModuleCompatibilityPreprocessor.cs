using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DbUp.Engine;

namespace Full.NET.Migrations.DbUp;

/// <summary>固定预设下仅执行所选建表脚本对应的历史 UUID、状态和租约变更，不改写历史资源。</summary>
internal sealed partial class PresetPublishedModuleCompatibilityPreprocessor(IReadOnlySet<string>? selectedScripts)
    : IScriptPreprocessor
{
    private const string MySqlDigest = "EA50F03F9E3C09FDC2762965EE65C09DDE193136D8C3C79E48B2A7583A94C909";
    private const string SqlServerDigest = "978B6F39B576FB5A0A5EAAFFFCC947ACC424E50A17D131378299E4C7798D3739";
    private const string MySql206Digest = "C779DEFFE5FEA7D098CA438EA8BC621EAEF7DDDBF3BFF26C095EB361474A1E21";
    private const string MySql207Digest = "85F9F8E8003D4D71545322B9FA5F0FB98D8ADA4A7652E99A3288406ECB7EB11C";
    private const string SqlServer206Digest = "BBF677F9CB71762BF771F215CB3DE8D7AE730DCFD640686FF5AB83C1BB76A484";
    private const string SqlServer207Digest = "DF138A1B5A53224655B2DAAF7E96134AD06A2CABF39D7536B32CD109D2339D05";

    private static readonly IReadOnlyDictionary<string, string> TableOrigins = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["fn_document_version_deletion_audit"] = "165_DocumentVersionDeletionAudit.sql",
        ["fn_document_access_log"] = "167_DocumentAccessLog.sql",
        ["fn_document_preview_task"] = "169_DocumentPreviewTask.sql",
        ["fn_import_export_task"] = "171_ImportExportTask.sql",
        ["fn_reporting_data_source"] = "175_ReportingDataSource.sql",
        ["fn_reporting_export_task"] = "180_ReportingExportTask.sql",
        ["fn_printing_template"] = "182_PrintingTemplate.sql",
        ["fn_printing_template_version"] = "182_PrintingTemplate.sql",
        ["fn_ai_model_config"] = "184_AiModelConfig.sql",
        ["fn_ai_tenant_quota"] = "184_AiModelConfig.sql",
        ["fn_ai_chat_session"] = "186_AiChatSession.sql",
        ["fn_ai_chat_message"] = "186_AiChatSession.sql",
        ["fn_ai_agent_tool_call"] = "188_AiAgentToolCall.sql",
        ["fn_payment_merchant_config"] = "190_PaymentMerchantConfig.sql",
        ["fn_payment_order"] = "190_PaymentMerchantConfig.sql",
        ["fn_payment_notify_receipt"] = "192_PaymentNotifyRefund.sql",
        ["fn_payment_refund"] = "192_PaymentNotifyRefund.sql",
        ["fn_goview_project"] = "195_GoViewProject.sql",
        ["fn_goview_project_version"] = "195_GoViewProject.sql",
        ["fn_k3cloud_connection_config"] = "197_K3CloudFoundation.sql",
        ["fn_k3cloud_document_sync"] = "197_K3CloudFoundation.sql",
        ["fn_ocr_provider_config"] = "199_OcrFoundation.sql",
        ["fn_ocr_id_card_task"] = "199_OcrFoundation.sql",
    };

    /// <summary>全文摘要和固定清单共同限定兼容变换；未选择的官方表若已存在则拒绝预设漂移。</summary>
    public string Process(string contents)
    {
        ArgumentNullException.ThrowIfNull(contents);
        if (selectedScripts is null) return contents;
        var normalized = contents.ReplaceLineEndings("\n");
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        if (digest is MySql206Digest or MySql207Digest)
            return PreserveOriginal(contents, normalized, ProcessSharedModuleScript(normalized, MySqlSharedBlock(), isMySql: true));
        if (digest == SqlServer206Digest)
            return PreserveOriginal(contents, normalized, ProcessSharedModuleScript(normalized, SqlServer206Block(), isMySql: false));
        if (digest == SqlServer207Digest)
            return PreserveOriginal(contents, normalized, ProcessSharedModuleScript(normalized, SqlServer207Block(), isMySql: false));
        if (digest == SqlServerDigest)
        {
            var sqlServerResult = SqlServerInvariantBlock().Replace(normalized, match =>
            {
                var table = match.Groups["table"].Value;
                return IsSelected(table) ? match.Value
                    : $"IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL\n"
                        + $"    THROW 51203, 'Preset migration inventory excludes existing table: {table}', 1;\n";
            });
            return PreserveOriginal(contents, normalized, sqlServerResult);
        }
        if (digest != MySqlDigest) return contents;

        var excludedTables = new HashSet<string>(StringComparer.Ordinal);
        var result = MySqlTableBlock().Replace(normalized, match =>
        {
            var table = MySqlTableName().Match(match.Value).Groups["table"].Value;
            if (IsSelected(table)) return match.Value;
            if (!excludedTables.Add(table)) return "";
            // 不能用“表不存在就跳过”掩盖所选模块缺表；只有清单未选且数据库也不存在才允许省略。
            return "INSERT INTO fn_migrations_uuid_preflight (InvalidCount)\n"
                + "SELECT COUNT(*) FROM information_schema.TABLES\n"
                + $"WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}';\n\n";
        });
        return PreserveOriginal(contents, normalized, result);
    }

    // 全量或当前脚本所需表均被选择时，保持历史脚本连同换行符逐字节不变。
    private static string PreserveOriginal(string original, string normalized, string processed) =>
        processed == normalized ? original : processed;

    /// <summary>先检查未选表确实不存在，再保留所选模块原始 DDL；所选表缺失仍由原脚本报错。</summary>
    private string ProcessSharedModuleScript(string contents, Regex blocks, bool isMySql)
    {
        var excluded = new HashSet<string>(StringComparer.Ordinal);
        var result = blocks.Replace(contents, match =>
        {
            var table = (isMySql ? MySqlTableName() : SqlServerTableName())
                .Match(match.Value).Groups["table"].Value;
            if (IsSelected(table)) return match.Value;
            excluded.Add(table);
            return "";
        });
        if (excluded.Count == 0) return result;
        var guard = new StringBuilder();
        if (isMySql)
            guard.Append("DROP TEMPORARY TABLE IF EXISTS fn_migrations_preset_scope_guard;\n")
                .Append("CREATE TEMPORARY TABLE fn_migrations_preset_scope_guard (\n")
                .Append("    InvalidCount int NOT NULL COMMENT '未选模块已存在表数量', CHECK (InvalidCount = 0));\n");
        foreach (var table in excluded.Order(StringComparer.Ordinal))
        {
            if (isMySql)
                guard.Append("INSERT INTO fn_migrations_preset_scope_guard (InvalidCount)\n")
                    .Append("SELECT COUNT(*) FROM information_schema.TABLES\n")
                    .Append($"WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}';\n");
            else
                guard.Append($"IF OBJECT_ID(N'dbo.{table}', N'U') IS NOT NULL\n")
                    .Append($"    THROW 51203, 'Preset migration inventory excludes existing table: {table}', 1;\n");
        }
        if (isMySql) guard.Append("DROP TEMPORARY TABLE fn_migrations_preset_scope_guard;\n");
        return guard.Append('\n').Append(result).ToString();
    }

    private bool IsSelected(string table)
    {
        if (!TableOrigins.TryGetValue(table, out var script))
            throw new InvalidOperationException($"Unknown published module migration table: {table}");
        return selectedScripts!.Contains(script);
    }

    [GeneratedRegex(@"(?m)^IF NOT EXISTS \(SELECT 1 FROM sys\.columns WHERE object_id = OBJECT_ID\(N'dbo\.(?<table>fn_[a-z0-9_]+)'\)\n[^\n]+\n    THROW 51203, '[^'\n]+', 1;\n?")]
    private static partial Regex SqlServerInvariantBlock();

    [GeneratedRegex(@"(?ms)^-- (?:预检|转换) fn_[a-z0-9_]+\.[^\n]+\n.*?(?=^-- |\z)|^-- (?:只暂时移除本模块已知文档约束|恢复本模块文档关联)[^\n]+\n.*?DEALLOCATE PREPARE uuid_stmt;\n*")]
    private static partial Regex MySqlTableBlock();

    [GeneratedRegex(@"TABLE_NAME = '(?<table>fn_[a-z0-9_]+)'")]
    private static partial Regex MySqlTableName();

    [GeneratedRegex(@"(?ms)^SET @(?:constraint_exists|column_ddl|index_ddl) :=.*?DEALLOCATE PREPARE (?:stmt|column_stmt|index_stmt);\n*")]
    private static partial Regex MySqlSharedBlock();

    [GeneratedRegex(@"(?ms)^IF EXISTS \(.*?(?=^IF EXISTS \(|\z)")]
    private static partial Regex SqlServer206Block();

    [GeneratedRegex(@"(?ms)^IF COL_LENGTH\(N'dbo\.(?<table>fn_[a-z0-9_]+)', N'LeaseId'\) IS NULL.*?(?=^IF COL_LENGTH\(N'dbo\.fn_[a-z0-9_]+', N'LeaseId'\) IS NULL|\z)")]
    private static partial Regex SqlServer207Block();

    [GeneratedRegex(@"(?:OBJECT_ID|COL_LENGTH)\(N'dbo\.(?<table>fn_[a-z0-9_]+)'")]
    private static partial Regex SqlServerTableName();
}
